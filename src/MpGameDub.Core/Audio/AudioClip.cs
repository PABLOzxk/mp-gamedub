namespace MpGameDub.Core.Audio;

/// <summary>Áudio inteiro na memória, com os canais intercalados.</summary>
public sealed record AudioClip(float[] Samples, int SampleRate, int Channels)
{
    public int Frames => Samples.Length / Channels;
    public TimeSpan Duration => TimeSpan.FromSeconds((double)Frames / SampleRate);

    public AudioClip ToMono() => Channels == 1 ? this : new AudioClip(AudioMath.ToMono(Samples, Channels), SampleRate, 1);

    public AudioClip Resample(int rate)
    {
        var mono = ToMono();
        return rate == SampleRate ? mono : new AudioClip(StreamingResampler.Convert(mono.Samples, SampleRate, rate), rate, 1);
    }
}
