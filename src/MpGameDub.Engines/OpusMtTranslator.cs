using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using MpGameDub.Core.Speech;

namespace MpGameDub.Engines;

/// <summary>
/// Tradução Opus-MT (Marian) exportada para ONNX pelo Optimum (ferramentas/exportar-opus-mt.py), com o cache de
/// atenção do decodificador: decoder_model no primeiro passo, decoder_with_past_model nos seguintes. Busca gulosa,
/// mais rápida que a busca em feixe e boa o bastante para frases curtas de jogo. O tc-big en-pt pede o código do
/// idioma de destino na frente do texto: "&gt;&gt;pob&lt;&lt;" é português do Brasil, "&gt;&gt;por&lt;&lt;" o de Portugal.
/// </summary>
public sealed partial class OpusMtTranslator : ITranslator
{
    private readonly InferenceSession _encoder;
    private readonly InferenceSession _decoder;
    private readonly InferenceSession _decoderWithPast;
    private readonly SentencePieceTokenizer _source;
    private readonly Dictionary<string, int> _vocab;
    private readonly string[] _pieces;
    private readonly string? _targetCode;
    private readonly int _eos;
    private readonly int _pad;
    private readonly int _unk;
    private readonly int _start;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public OpusMtTranslator(string folder, ComputeDevice device, int cpuThreads, string? targetCode = ">>pob<<")
    {
        Name = $"Opus-MT en-pt ({(device == ComputeDevice.Gpu ? "placa" : "CPU")})";
        using var options = OnnxSessions.Create(device, cpuThreads);
        _encoder = new InferenceSession(Path.Combine(folder, "encoder_model.onnx"), options);
        _decoder = new InferenceSession(Path.Combine(folder, "decoder_model.onnx"), options);
        _decoderWithPast = new InferenceSession(Path.Combine(folder, "decoder_with_past_model.onnx"), options);

        using (var spm = File.OpenRead(Path.Combine(folder, "source.spm")))
            _source = SentencePieceTokenizer.Create(spm, addBeginningOfSentence: false, addEndOfSentence: false);
        _vocab = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(Path.Combine(folder, "vocab.json")))
            ?? throw new InvalidDataException("vocab.json vazio.");
        _pieces = new string[_vocab.Values.Max() + 1];
        foreach (var (piece, id) in _vocab) _pieces[id] = piece;
        _unk = _vocab["<unk>"];
        using (var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "config.json"))))
        {
            _eos = config.RootElement.GetProperty("eos_token_id").GetInt32();
            _pad = config.RootElement.GetProperty("pad_token_id").GetInt32();
            _start = config.RootElement.GetProperty("decoder_start_token_id").GetInt32();
        }

        if (targetCode is not null && !_vocab.ContainsKey(targetCode))
            throw new InvalidDataException($"O modelo não conhece o código de idioma {targetCode}.");
        _targetCode = targetCode;
    }

    public string Name { get; }

    public async Task<string> TranslateAsync(string text, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await Task.Run(() => Translate(text, ct), ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal int[] Encode(string text)
    {
        var ids = new List<int>();
        if (_targetCode is not null) ids.Add(_vocab[_targetCode]);
        var clean = LanguageCode().Replace(text, "").Trim();
        foreach (var token in _source.EncodeToTokens(clean, out _, addBeginningOfSentence: false, addEndOfSentence: false))
            ids.Add(_vocab.GetValueOrDefault(token.Value, _unk));
        ids.Add(_eos);
        return ids.ToArray();
    }

    internal string Decode(IEnumerable<int> ids)
    {
        var pieces = ids.Where(id => id != _eos && id != _pad && id < _pieces.Length).Select(id => _pieces[id]);
        return string.Concat(pieces).Replace('▁', ' ').Trim();
    }

    private string Translate(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var input = Encode(text);
        var length = input.Length;
        var inputIds = new DenseTensor<long>(input.Select(i => (long)i).ToArray(), [1, length]);
        var mask = new DenseTensor<long>(Enumerable.Repeat(1L, length).ToArray(), [1, length]);

        using var encoded = _encoder.Run([
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", mask),
        ]);
        var hidden = encoded.First().AsTensor<float>().ToDenseTensor();

        var output = new List<int>();
        var past = new Dictionary<string, DenseTensor<float>>();
        var next = _start;
        var maxSteps = MaxSteps(length);
        for (var step = 0; step < maxSteps; step++)
        {
            ct.ThrowIfCancellationRequested();
            var first = step == 0;
            var session = first ? _decoder : _decoderWithPast;
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(new[] { (long)next }, [1, 1])),
                NamedOnnxValue.CreateFromTensor("encoder_attention_mask", mask),
            };
            if (session.InputMetadata.ContainsKey("encoder_hidden_states"))
                inputs.Add(NamedOnnxValue.CreateFromTensor("encoder_hidden_states", hidden));
            if (!first)
                foreach (var name in session.InputMetadata.Keys.Where(k => k.StartsWith("past_key_values.", StringComparison.Ordinal)))
                    inputs.Add(NamedOnnxValue.CreateFromTensor(name, past[name]));

            using var results = session.Run(inputs);
            foreach (var r in results.Where(r => r.Name.StartsWith("present.", StringComparison.Ordinal)))
                past["past_key_values." + r.Name["present.".Length..]] = r.AsTensor<float>().ToDenseTensor();

            next = ArgMax(results.First(r => r.Name == "logits").AsTensor<float>());
            if (next == _eos) break;
            output.Add(next);
        }
        return Decode(output);
    }

    // Limite de segurança contra o modelo repetir sem parar; o script de exportação usa o mesmo.
    internal static int MaxSteps(int inputLength) => Math.Min(512, 2 * inputLength + 16);

    // Último passo de [1, 1, vocabulário]; o token de preenchimento nunca é escolhido (bad_words_ids do Marian).
    private int ArgMax(Tensor<float> logits)
    {
        var size = logits.Dimensions[^1];
        var offset = (int)(logits.Length - size);
        var dense = logits.ToDenseTensor().Buffer.Span.Slice(offset, size);
        var best = -1;
        var bestValue = float.NegativeInfinity;
        for (var i = 0; i < dense.Length; i++)
        {
            if (i == _pad) continue;
            if (dense[i] > bestValue)
            {
                bestValue = dense[i];
                best = i;
            }
        }
        return best;
    }

    [GeneratedRegex(@"^\s*>>[a-z_]+<<\s*")]
    private static partial Regex LanguageCode();

    public void Dispose()
    {
        _encoder.Dispose();
        _decoder.Dispose();
        _decoderWithPast.Dispose();
        _gate.Dispose();
    }
}
