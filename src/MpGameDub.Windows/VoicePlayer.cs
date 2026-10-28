using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MpGameDub.Core.Pipeline;
using MpGameDub.Core.Speech;
using NAudio.Wave;

namespace MpGameDub.Windows;

/// <summary>
/// Toca a voz traduzida na saída padrão, uma fala atrás da outra. O Windows converte os 24 kHz da voz para o
/// formato da placa de som. Devolve quando cada fala vai começar a soar, contando o que já estava na fila e a
/// latência da saída, para medir o atraso real.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class VoicePlayer : IVoiceOutput, IDisposable
{
    private readonly WasapiPlayer _player;
    private readonly BufferedWaveProvider _buffer;
    private readonly int _rate;

    public VoicePlayer(int sampleRate, float volume = 1f)
    {
        _rate = sampleRate;
        _buffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1), TimeSpan.FromMinutes(2))
        {
            ReadFully = true, // sem fala na fila, toca silêncio e a saída continua aberta (sem estalo ao recomeçar)
            DiscardOnBufferOverflow = true,
        };
        _player = new WasapiPlayerBuilder().WithSharedMode().WithLatency(60).Build();
        _player.Init(_buffer);
        _player.Volume = Math.Clamp(volume, 0f, 1f);
        _player.Play();
    }

    public TimeSpan OutputLatency => _player.AverageLatency;

    /// <summary>O que ainda falta tocar.</summary>
    public TimeSpan Pending => _buffer.BufferedDuration;

    /// <summary>Quando (Stopwatch) uma fala posta na fila agora começaria a soar.</summary>
    public long NextStartAt => Stopwatch.GetTimestamp() + (long)((_buffer.BufferedDuration + OutputLatency).TotalSeconds * Stopwatch.Frequency);

    /// <summary>Põe a fala na fila e devolve o Stopwatch de quando ela deve começar a soar.</summary>
    public long Enqueue(SynthesizedSpeech speech)
    {
        if (speech.SampleRate != _rate) throw new ArgumentException($"A voz deve vir em {_rate} Hz.", nameof(speech));
        var startsAt = NextStartAt;
        _buffer.AddSamples(MemoryMarshal.AsBytes(speech.Samples.AsSpan()));
        return startsAt;
    }

    public void Clear() => _buffer.ClearBuffer();

    public void Dispose()
    {
        _player.Stop();
        _player.Dispose();
    }
}
