using MpGameDub.Core.Audio;

namespace MpGameDub.Tests.Audio;

public class WavFileTests
{
    [Fact]
    public void Pcm16_RoundTrip_KeepsFormatAndSamples()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mpgd-{Guid.NewGuid():N}.wav");
        try
        {
            float[] samples = [0f, 0.5f, -0.5f, 1f, -1f, 0.25f];
            WavFile.WritePcm16(path, samples, 48000, channels: 2);

            var clip = WavFile.Read(path);

            Assert.Equal(48000, clip.SampleRate);
            Assert.Equal(2, clip.Channels);
            Assert.Equal(3, clip.Frames);
            for (var i = 0; i < samples.Length; i++) Assert.Equal(samples[i], clip.Samples[i], 1e-4f);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Float32_IsRead()
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            float[] data = [0.1f, -0.2f, 0.3f];
            w.Write("RIFF"u8); w.Write(36u + 12); w.Write("WAVE"u8);
            w.Write("fmt "u8); w.Write(16u); w.Write((ushort)3); w.Write((ushort)1); w.Write(16000); w.Write(64000); w.Write((ushort)4); w.Write((ushort)32);
            w.Write("data"u8); w.Write(12u);
            foreach (var f in data) w.Write(f);
        }
        stream.Position = 0;

        var clip = WavFile.Read(stream);

        Assert.Equal([0.1f, -0.2f, 0.3f], clip.Samples);
        Assert.Equal(16000, clip.SampleRate);
    }

    [Fact]
    public void NotAWav_IsRejected()
    {
        using var stream = new MemoryStream("hello world, not audio"u8.ToArray());
        Assert.Throws<InvalidDataException>(() => WavFile.Read(stream));
    }

    [Fact]
    public void ToMono_AveragesChannels()
    {
        Assert.Equal([0.5f, 0f], AudioMath.ToMono([1f, 0f, 0.5f, -0.5f], 2));
    }
}
