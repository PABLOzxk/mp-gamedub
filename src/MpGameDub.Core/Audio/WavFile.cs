using System.Buffers.Binary;
using System.Text;

namespace MpGameDub.Core.Audio;

/// <summary>Lê e grava WAV. Lê PCM de 16, 24 e 32 bits e float de 32 bits; grava PCM de 16 bits.</summary>
public static class WavFile
{
    private const ushort FormatPcm = 1;
    private const ushort FormatFloat = 3;
    private const ushort FormatExtensible = 0xFFFE;

    public static AudioClip Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public static AudioClip Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        if (Ascii(reader.ReadBytes(4)) != "RIFF") throw new InvalidDataException("Não é um arquivo WAV (falta RIFF).");
        reader.ReadUInt32();
        if (Ascii(reader.ReadBytes(4)) != "WAVE") throw new InvalidDataException("Não é um arquivo WAV (falta WAVE).");

        ushort format = 0, channels = 0, bits = 0;
        var rate = 0;
        byte[]? data = null;
        while (stream.Position + 8 <= stream.Length)
        {
            var id = Ascii(reader.ReadBytes(4));
            var size = reader.ReadUInt32();
            var next = stream.Position + size + (size & 1);
            if (id == "fmt ")
            {
                format = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                rate = reader.ReadInt32();
                reader.ReadUInt32();
                reader.ReadUInt16();
                bits = reader.ReadUInt16();
                if (format == FormatExtensible && size >= 40)
                {
                    reader.ReadUInt16();
                    reader.ReadUInt16();
                    reader.ReadUInt32();
                    format = reader.ReadUInt16(); // os 2 primeiros bytes do GUID do subformato dizem PCM ou float
                }
            }
            else if (id == "data")
            {
                var available = (int)Math.Min(size, stream.Length - stream.Position);
                data = reader.ReadBytes(available);
            }
            if (next > stream.Length) break;
            stream.Position = next;
        }

        if (channels == 0 || rate <= 0) throw new InvalidDataException("WAV sem o bloco de formato.");
        if (data is null) throw new InvalidDataException("WAV sem o bloco de dados.");
        return new AudioClip(Decode(data, format, bits), rate, channels);
    }

    public static void WritePcm16(string path, ReadOnlySpan<float> samples, int sampleRate, int channels = 1)
    {
        using var writer = new WavWriter(path, sampleRate, channels);
        writer.Write(samples);
    }

    private static float[] Decode(byte[] data, ushort format, ushort bits)
    {
        switch (format, bits)
        {
            case (FormatPcm, 16):
                return AudioMath.Pcm16ToFloat(data);
            case (FormatPcm, 24):
            {
                var samples = new float[data.Length / 3];
                for (var i = 0; i < samples.Length; i++)
                {
                    var value = data[3 * i] | (data[3 * i + 1] << 8) | (data[3 * i + 2] << 16);
                    if ((value & 0x800000) != 0) value |= unchecked((int)0xFF000000);
                    samples[i] = value / 8388608f;
                }
                return samples;
            }
            case (FormatPcm, 32):
            {
                var samples = new float[data.Length / 4];
                for (var i = 0; i < samples.Length; i++)
                    samples[i] = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4 * i)) / 2147483648f;
                return samples;
            }
            case (FormatFloat, 32):
            {
                var samples = new float[data.Length / 4];
                Buffer.BlockCopy(data, 0, samples, 0, samples.Length * 4);
                return samples;
            }
            default:
                throw new NotSupportedException($"Formato de WAV não suportado (formato {format}, {bits} bits).");
        }
    }

    private static string Ascii(byte[] bytes) => Encoding.ASCII.GetString(bytes);
}

/// <summary>
/// Grava PCM de 16 bits aos poucos (para gravações longas). O cabeçalho é corrigido com os tamanhos
/// reais ao fechar; se o programa cair no meio, o arquivo fica com tamanho zero no cabeçalho, mas os dados estão lá.
/// </summary>
public sealed class WavWriter : IDisposable
{
    private readonly FileStream _stream;
    private readonly BinaryWriter _writer;
    private long _dataBytes;
    private bool _disposed;

    public WavWriter(string path, int sampleRate, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        SampleRate = sampleRate;
        Channels = channels;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        _writer = new BinaryWriter(_stream, Encoding.ASCII, leaveOpen: true);
        WriteHeader();
    }

    public int SampleRate { get; }
    public int Channels { get; }
    public TimeSpan Duration => TimeSpan.FromSeconds(_dataBytes / 2.0 / Channels / SampleRate);

    public void Write(ReadOnlySpan<float> samples)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var buffer = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var value = (short)Math.Clamp(MathF.Round(samples[i] * 32767f), short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(2 * i), value);
        }
        _writer.Write(buffer);
        _dataBytes += buffer.Length;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _writer.Flush();
        _stream.Position = 4;
        _writer.Write((uint)Math.Min(uint.MaxValue, 36 + _dataBytes));
        _stream.Position = 40;
        _writer.Write((uint)Math.Min(uint.MaxValue, _dataBytes));
        _writer.Dispose();
        _stream.Dispose();
    }

    private void WriteHeader()
    {
        _writer.Write("RIFF"u8);
        _writer.Write(0u);
        _writer.Write("WAVE"u8);
        _writer.Write("fmt "u8);
        _writer.Write(16u);
        _writer.Write((ushort)1);
        _writer.Write((ushort)Channels);
        _writer.Write(SampleRate);
        _writer.Write(SampleRate * Channels * 2);
        _writer.Write((ushort)(Channels * 2));
        _writer.Write((ushort)16);
        _writer.Write("data"u8);
        _writer.Write(0u);
    }
}
