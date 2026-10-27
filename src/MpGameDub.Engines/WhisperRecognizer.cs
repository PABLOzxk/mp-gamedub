using System.Text;
using MpGameDub.Core.Speech;
using Whisper.net;

namespace MpGameDub.Engines;

/// <summary>
/// Whisper (whisper.cpp) pelo Whisper.net. Qual biblioteca nativa é usada (CUDA, Vulkan ou CPU) é decidido
/// uma vez por processo, na primeira carga; para comparar placa e processador, rode o teste duas vezes.
/// </summary>
public sealed class WhisperRecognizer : ISpeechRecognizer
{
    private readonly WhisperFactory _factory;
    private readonly WhisperProcessor _processor;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WhisperRecognizer(string modelPath, string language, bool useGpu, int cpuThreads)
    {
        Name = $"Whisper {Path.GetFileNameWithoutExtension(modelPath).Replace("ggml-", "")} ({(useGpu ? "placa" : "CPU")})";
        _factory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions { UseGpu = useGpu });
        _processor = _factory.CreateBuilder()
            .WithLanguage(language)
            .WithThreads(cpuThreads)
            .WithNoContext()
            .WithSingleSegment()
            .Build();
    }

    public string Name { get; }

    public async Task<Transcript> RecognizeAsync(float[] samples16k, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var text = new StringBuilder();
            string? language = null;
            await foreach (var segment in _processor.ProcessAsync(samples16k, ct))
            {
                text.Append(segment.Text);
                language ??= segment.Language;
            }
            return new Transcript(text.ToString().Trim(), language);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        // Depois de um tempo esgotado, a fala cancelada ainda está saindo do whisper.cpp numa thread do Whisper.net
        // (ele aborta no próximo passo do cálculo). O Dispose dele recusa fechar nesse meio-tempo; o DisposeAsync espera.
        _processor.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _factory.Dispose();
        _gate.Dispose();
    }
}
