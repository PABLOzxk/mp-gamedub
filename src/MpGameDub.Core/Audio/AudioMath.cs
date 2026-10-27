namespace MpGameDub.Core.Audio;

public static class AudioMath
{
    /// <summary>Junta os canais de um bloco intercalado (E D E D ...) num canal só, pela média.</summary>
    public static float[] ToMono(ReadOnlySpan<float> interleaved, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        var frames = interleaved.Length / channels;
        var mono = new float[frames];
        if (channels == 1)
        {
            interleaved[..frames].CopyTo(mono);
            return mono;
        }
        for (var f = 0; f < frames; f++)
        {
            var offset = f * channels;
            var sum = 0f;
            for (var c = 0; c < channels; c++) sum += interleaved[offset + c];
            mono[f] = sum / channels;
        }
        return mono;
    }

    public static float Rms(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return 0;
        double sum = 0;
        foreach (var s in samples) sum += (double)s * s;
        return (float)Math.Sqrt(sum / samples.Length);
    }

    public static double ToDecibels(double linear) => linear <= 1e-10 ? -200 : 20 * Math.Log10(linear);

    /// <summary>Converte PCM de 16 bits (little-endian) para float entre -1 e 1.</summary>
    public static float[] Pcm16ToFloat(ReadOnlySpan<byte> bytes)
    {
        var samples = new float[bytes.Length / 2];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = (short)(bytes[2 * i] | (bytes[2 * i + 1] << 8)) / 32768f;
        return samples;
    }
}
