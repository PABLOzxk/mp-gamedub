using MpGameDub.Core.Audio;
using MpGameDub.Core.Speech;

namespace MpGameDub.Tests.Pipeline;

/// <summary>VAD de teste: fala é qualquer quadro com energia acima de um limiar.</summary>
internal sealed class EnergyVad : IVoiceActivityDetector
{
    public int FrameSize => 512;
    public float Process(ReadOnlySpan<float> frame) => AudioMath.Rms(frame) > 0.05f ? 0.9f : 0.05f;
    public void Reset() { }
    public void Dispose() { }
}

internal sealed class FakeRecognizer(Func<float[], string> text, TimeSpan delay = default) : ISpeechRecognizer
{
    public string Name => "reconhecedor de teste";
    public int Calls { get; private set; }

    public async Task<Transcript> RecognizeAsync(float[] samples16k, CancellationToken ct)
    {
        Calls++;
        if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
        return new Transcript(text(samples16k), "en");
    }

    public void Dispose() { }
}

internal sealed class FakeTranslator(Func<string, string>? translate = null, Exception? error = null) : ITranslator
{
    public string Name => "tradutor de teste";

    public Task<string> TranslateAsync(string text, CancellationToken ct) =>
        error is not null ? Task.FromException<string>(error) : Task.FromResult(translate?.Invoke(text) ?? "[pt] " + text);

    public void Dispose() { }
}

internal sealed class FakeSynthesizer(TimeSpan voiceLength) : ISpeechSynthesizer
{
    public string Name => "voz de teste";
    public List<string> Spoken { get; } = [];

    public Task<SynthesizedSpeech> SynthesizeAsync(string text, CancellationToken ct)
    {
        Spoken.Add(text);
        var samples = new float[(int)(voiceLength.TotalSeconds * 24000)];
        Array.Fill(samples, 0.5f);
        return Task.FromResult(new SynthesizedSpeech(samples, 24000));
    }

    public void Dispose() { }
}

internal static class TestAudio
{
    /// <summary>Áudio de 16 kHz com "falas" (seno forte) nos trechos pedidos e silêncio no resto.</summary>
    public static AudioClip WithSpeech(TimeSpan total, params (double Start, double End)[] speech)
    {
        var samples = new float[(int)(total.TotalSeconds * 16000)];
        foreach (var (start, end) in speech)
            for (var i = (int)(start * 16000); i < Math.Min(samples.Length, (int)(end * 16000)); i++)
                samples[i] = 0.5f * MathF.Sin(2 * MathF.PI * 220 * i / 16000);
        return new AudioClip(samples, 16000, 1);
    }
}
