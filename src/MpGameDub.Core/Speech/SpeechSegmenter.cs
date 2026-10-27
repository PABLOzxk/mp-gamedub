namespace MpGameDub.Core.Speech;

public sealed record SegmenterOptions
{
    /// <summary>Probabilidade a partir da qual um quadro conta como fala e abre um trecho.</summary>
    public float StartThreshold { get; init; } = 0.5f;

    /// <summary>Abaixo disto o quadro conta como silêncio. Fica abaixo do limiar de início para o trecho não piscar.</summary>
    public float EndThreshold { get; init; } = 0.35f;

    /// <summary>Silêncio necessário para dar a fala por encerrada. É o maior custo fixo de latência: menos corta frases no meio.</summary>
    public int MinSilenceMs { get; init; } = 400;

    /// <summary>Trechos com menos fala que isto são descartados (estalos, passos, tiros).</summary>
    public int MinSpeechMs { get; init; } = 250;

    /// <summary>Falas mais longas são cortadas aqui, para não esperar um monólogo inteiro.</summary>
    public int MaxSpeechMs { get; init; } = 12000;

    /// <summary>Áudio de antes do primeiro quadro de fala que entra no trecho, para não perder o começo da palavra.</summary>
    public int PrePadMs { get; init; } = 160;

    /// <summary>Silêncio mantido depois da última sílaba.</summary>
    public int PostPadMs { get; init; } = 96;
}

/// <summary>
/// Um trecho de fala, com posições contadas em amostras de 16 kHz desde o início do áudio. EndSample é o fim do
/// trecho entregue (com a folga depois da última sílaba); VoiceEndSample é onde a voz parou de verdade, e é dele
/// que a latência é contada.
/// </summary>
public sealed record SpeechSegment(float[] Samples, long StartSample, long EndSample, long VoiceEndSample, long ConfirmedAtSample, bool ForcedCut)
{
    public TimeSpan Start => FromSamples(StartSample);
    public TimeSpan End => FromSamples(EndSample);
    public TimeSpan VoiceEnd => FromSamples(VoiceEndSample);
    public TimeSpan Duration => FromSamples(EndSample - StartSample);

    /// <summary>Quanto depois do fim da voz o detector teve certeza de que a fala acabou.</summary>
    public TimeSpan ConfirmationDelay => FromSamples(ConfirmedAtSample - VoiceEndSample);

    private static TimeSpan FromSamples(long samples) => TimeSpan.FromSeconds((double)samples / SpeechAudio.SampleRate);
}

/// <summary>
/// Junta quadros com a probabilidade de fala dada pelo VAD em trechos prontos para o reconhecimento.
/// Usa histerese (um limiar para abrir, outro menor para fechar) e só fecha depois de um silêncio mínimo.
/// </summary>
public sealed class SpeechSegmenter
{
    private readonly SegmenterOptions _options;
    private readonly int _frameSize;
    private readonly Queue<float[]> _recent = new();
    private readonly List<float> _buffer = [];
    private readonly int _prePadFrames;
    private readonly int _postPadSamples;
    private readonly int _minSilenceFrames;
    private readonly int _minSpeechSamples;
    private readonly int _maxSpeechSamples;

    private long _position;
    private bool _inSpeech;
    private long _segmentStart;
    private long _voiceStart;
    private int _silentFrames;
    private long _lastVoiceEnd;

    public SpeechSegmenter(int frameSize, SegmenterOptions? options = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(frameSize, 1);
        _options = options ?? new SegmenterOptions();
        if (_options.EndThreshold > _options.StartThreshold)
            throw new ArgumentException("O limiar de fim não pode ser maior que o de início.", nameof(options));
        _frameSize = frameSize;
        _prePadFrames = MsToSamples(_options.PrePadMs) / frameSize;
        _postPadSamples = MsToSamples(_options.PostPadMs);
        _minSilenceFrames = Math.Max(1, (int)Math.Ceiling(MsToSamples(_options.MinSilenceMs) / (double)frameSize));
        _minSpeechSamples = MsToSamples(_options.MinSpeechMs);
        _maxSpeechSamples = MsToSamples(_options.MaxSpeechMs);
    }

    public bool InSpeech => _inSpeech;

    /// <summary>Entrega um quadro e a probabilidade de fala dele. Devolve um trecho quando uma fala termina.</summary>
    public SpeechSegment? Push(ReadOnlySpan<float> frame, float probability)
    {
        if (frame.Length != _frameSize) throw new ArgumentException($"O quadro deve ter {_frameSize} amostras.", nameof(frame));
        var frameStart = _position;
        _position += _frameSize;
        SpeechSegment? result = null;

        if (!_inSpeech)
        {
            if (probability >= _options.StartThreshold)
            {
                _inSpeech = true;
                _segmentStart = frameStart - _recent.Count * (long)_frameSize;
                foreach (var old in _recent) _buffer.AddRange(old);
                _recent.Clear();
                _buffer.AddRange(frame);
                _voiceStart = frameStart;
                _silentFrames = 0;
                _lastVoiceEnd = _position;
            }
            else
            {
                _recent.Enqueue(frame.ToArray());
                while (_recent.Count > _prePadFrames) _recent.Dequeue();
            }
            return null;
        }

        _buffer.AddRange(frame);
        if (probability >= _options.EndThreshold)
        {
            _silentFrames = 0;
            _lastVoiceEnd = _position;
        }
        else
        {
            _silentFrames++;
        }

        if (_silentFrames >= _minSilenceFrames)
        {
            result = Close(forced: false);
            _inSpeech = false;
        }
        else if (_buffer.Count >= _maxSpeechSamples)
        {
            result = Close(forced: true);
            // A fala continua: o próximo trecho começa aqui, sem pré-roll.
            _segmentStart = _position;
            _voiceStart = _position;
            _lastVoiceEnd = _position;
            _silentFrames = 0;
        }
        return result;
    }

    /// <summary>Fecha a fala em andamento, se houver (fim do arquivo ou da captura).</summary>
    public SpeechSegment? Flush()
    {
        if (!_inSpeech) return null;
        _inSpeech = false;
        return Close(forced: false);
    }

    private SpeechSegment? Close(bool forced)
    {
        var end = forced ? _position : Math.Min(_position, _lastVoiceEnd + _postPadSamples);
        var length = (int)(end - _segmentStart);
        var voiced = _lastVoiceEnd - _voiceStart; // sem contar o pré-roll
        var samples = _buffer.GetRange(0, Math.Min(length, _buffer.Count)).ToArray();
        _buffer.Clear();
        _recent.Clear();
        if (!forced && voiced < _minSpeechSamples) return null;
        var segmentEnd = _segmentStart + samples.Length;
        return new SpeechSegment(samples, _segmentStart, segmentEnd, Math.Min(_lastVoiceEnd, segmentEnd), _position, forced);
    }

    private static int MsToSamples(int ms) => ms * SpeechAudio.SampleRate / 1000;
}

/// <summary>Junta pedaços de áudio de qualquer tamanho em quadros do tamanho que o VAD pede.</summary>
public sealed class FrameSlicer(int frameSize)
{
    private readonly float[] _pending = new float[frameSize];
    private int _count;

    public List<float[]> Push(ReadOnlySpan<float> samples)
    {
        var frames = new List<float[]>();
        while (!samples.IsEmpty)
        {
            var take = Math.Min(frameSize - _count, samples.Length);
            samples[..take].CopyTo(_pending.AsSpan(_count));
            _count += take;
            samples = samples[take..];
            if (_count == frameSize)
            {
                _count = 0;
                frames.Add((float[])_pending.Clone());
            }
        }
        return frames;
    }
}
