using MpGameDub.Core.Audio;

namespace MpGameDub.Tests.Audio;

public class StreamingResamplerTests
{
    private static float[] Sine(double frequency, int rate, double seconds, float amplitude = 0.5f) =>
        Enumerable.Range(0, (int)(rate * seconds)).Select(i => amplitude * (float)Math.Sin(2 * Math.PI * frequency * i / rate)).ToArray();

    // Conta trocas de sinal ignorando valores quase zero (a senoide amostrada cai em zero exato e oscila no ruído).
    private static int ZeroCrossings(ReadOnlySpan<float> s)
    {
        var count = 0;
        var sign = 0;
        foreach (var v in s)
        {
            var current = v > 0.01f ? 1 : v < -0.01f ? -1 : 0;
            if (current == 0) continue;
            if (sign != 0 && current != sign) count++;
            sign = current;
        }
        return count;
    }

    [Fact]
    public void Downsampling_KeepsSpeechBandToneAndLength()
    {
        var input = Sine(1000, 48000, 1.0);
        var output = StreamingResampler.Convert(input, 48000, 16000);

        Assert.InRange(output.Length, 15990, 16000);
        // 1 kHz = 2 cruzamentos por zero a cada 16 amostras; pula o começo, onde o filtro ainda está enchendo.
        Assert.InRange(ZeroCrossings(output.AsSpan(200)), 1972, 1977);
        Assert.InRange(AudioMath.Rms(output.AsSpan(200)), 0.34f, 0.36f);
    }

    [Fact]
    public void Downsampling_RemovesToneAboveTheNewNyquist()
    {
        // 12 kHz não cabe em 16 kHz (máximo 8 kHz). Sem filtro, voltaria como um tom de 4 kHz.
        var output = StreamingResampler.Convert(Sine(12000, 48000, 1.0), 48000, 16000);
        Assert.True(AudioMath.Rms(output.AsSpan(200)) < 0.005f);
    }

    [Theory]
    [InlineData(48000)]
    [InlineData(44100)]
    public void ChunkedProcessing_MatchesOneShot(int inputRate)
    {
        var input = Sine(440, inputRate, 0.5);
        var whole = StreamingResampler.Convert(input, inputRate, 16000);

        var resampler = new StreamingResampler(inputRate, 16000);
        var pieces = new List<float>();
        var random = new Random(7);
        for (var offset = 0; offset < input.Length;)
        {
            var size = Math.Min(random.Next(1, 997), input.Length - offset);
            pieces.AddRange(resampler.Process(input.AsSpan(offset, size)));
            offset += size;
        }

        Assert.Equal(whole.Length, pieces.Count);
        for (var i = 0; i < whole.Length; i++) Assert.Equal(whole[i], pieces[i], 1e-5f);
    }

    [Fact]
    public void SameRate_IsPassThrough()
    {
        var input = Sine(440, 16000, 0.1);
        Assert.Equal(input, StreamingResampler.Convert(input, 16000, 16000));
    }
}
