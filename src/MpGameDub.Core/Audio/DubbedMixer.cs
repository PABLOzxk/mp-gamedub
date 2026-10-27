using MpGameDub.Core.Speech;

namespace MpGameDub.Core.Audio;

public sealed record DuckingOptions
{
    /// <summary>Volume do jogo enquanto a voz traduzida toca (1 = sem abaixar).</summary>
    public float GameGain { get; init; } = 0.3f;
    public TimeSpan Attack { get; init; } = TimeSpan.FromMilliseconds(80);
    public TimeSpan Release { get; init; } = TimeSpan.FromMilliseconds(250);
}

/// <summary>
/// Monta o áudio de comparação: o som original, abaixado enquanto a voz traduzida fala, com a voz posta no
/// instante em que teria começado ao vivo. É o arquivo para ouvir e decidir se o resultado presta.
/// </summary>
public static class DubbedMixer
{
    public static AudioClip Mix(AudioClip original, IEnumerable<(TimeSpan Start, SynthesizedSpeech Voice)> voices, DuckingOptions ducking)
    {
        var rate = original.SampleRate;
        var channels = original.Channels;
        var frames = original.Frames;
        var placed = voices
            .Select(v => (Start: (long)(v.Start.TotalSeconds * rate), Samples: StreamingResampler.Convert(v.Voice.Samples, v.Voice.SampleRate, rate)))
            .Where(v => v.Start < frames)
            .ToList();

        var gain = new float[frames];
        Array.Fill(gain, 1f);
        var attack = (long)(ducking.Attack.TotalSeconds * rate);
        var release = (long)(ducking.Release.TotalSeconds * rate);
        foreach (var (start, samples) in placed)
        {
            var end = start + samples.Length;
            for (var f = Math.Max(0, start - attack); f < Math.Min(frames, end + release); f++)
            {
                var g = ducking.GameGain;
                if (f < start) g = Lerp(1f, ducking.GameGain, (float)(f - (start - attack)) / attack);
                else if (f >= end) g = Lerp(ducking.GameGain, 1f, (float)(f - end) / release);
                gain[f] = Math.Min(gain[f], g);
            }
        }

        var output = new float[original.Samples.Length];
        for (var f = 0; f < frames; f++)
            for (var c = 0; c < channels; c++)
                output[f * channels + c] = original.Samples[f * channels + c] * gain[f];

        foreach (var (start, samples) in placed)
            for (var i = 0; i < samples.Length && start + i < frames; i++)
                for (var c = 0; c < channels; c++)
                    output[(start + i) * channels + c] += samples[i];

        for (var i = 0; i < output.Length; i++) output[i] = Math.Clamp(output[i], -1f, 1f);
        return new AudioClip(output, rate, channels);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);
}
