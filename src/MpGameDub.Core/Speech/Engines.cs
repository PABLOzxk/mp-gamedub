namespace MpGameDub.Core.Speech;

/// <summary>Taxa de amostragem que VAD e Whisper esperam.</summary>
public static class SpeechAudio
{
    public const int SampleRate = 16000;
}

/// <summary>Detector de fala: devolve a probabilidade de haver voz num quadro de tamanho fixo.</summary>
public interface IVoiceActivityDetector : IDisposable
{
    int FrameSize { get; }
    float Process(ReadOnlySpan<float> frame);
    void Reset();
}

public sealed record Transcript(string Text, string? Language);

public interface ISpeechRecognizer : IDisposable
{
    string Name { get; }
    Task<Transcript> RecognizeAsync(float[] samples16k, CancellationToken ct);
}

public interface ITranslator : IDisposable
{
    string Name { get; }
    Task<string> TranslateAsync(string text, CancellationToken ct);
}

public sealed record SynthesizedSpeech(float[] Samples, int SampleRate)
{
    public TimeSpan Duration => TimeSpan.FromSeconds((double)Samples.Length / SampleRate);
}

public interface ISpeechSynthesizer : IDisposable
{
    string Name { get; }
    Task<SynthesizedSpeech> SynthesizeAsync(string text, CancellationToken ct);
}
