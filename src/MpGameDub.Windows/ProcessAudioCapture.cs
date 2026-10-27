using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Channels;
using MpGameDub.Core.Audio;
using MpGameDub.Core.Speech;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MpGameDub.Windows;

/// <summary>
/// Um pedaço do áudio do jogo, já em 16 kHz mono. CapturedAt é a hora (na escala do Stopwatch) em que o Windows
/// capturou o começo do pacote, e não a hora em que ele chegou ao programa: assim o buffer de captura não esconde atraso.
/// </summary>
public sealed record CapturedChunk(float[] Samples, long FirstSample, long CapturedAt);

/// <summary>
/// Captura só o som do processo do jogo (e dos filhos dele) pelo loopback de processo do Windows, o mesmo recurso
/// que programas de gravação usam. Não injeta nada no jogo e não pede administrador. O próprio MP GameDub e o
/// Discord ficam de fora, então a voz traduzida não volta para o reconhecimento.
/// Gravar o áudio em arquivo só acontece se recordPath for dado (o usuário pediu).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessAudioCapture : IAsyncDisposable
{
    private readonly WasapiRecorder _recorder;
    private readonly StreamingResampler _resampler;
    private readonly Channel<CapturedChunk> _chunks = Channel.CreateUnbounded<CapturedChunk>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly WavWriter? _recording;
    private readonly int _channels;
    private long _samples;
    private long _silentPackets;
    private long _glitches;

    private ProcessAudioCapture(WasapiRecorder recorder, string? recordPath)
    {
        _recorder = recorder;
        var format = recorder.WaveFormat;
        if (format.Encoding != WaveFormatEncoding.IeeeFloat || format.BitsPerSample != 32)
            throw new NotSupportedException($"Formato de captura inesperado: {format}.");
        _channels = format.Channels;
        _resampler = new StreamingResampler(format.SampleRate, SpeechAudio.SampleRate);
        if (recordPath is not null) _recording = new WavWriter(recordPath, format.SampleRate, format.Channels);
        _recorder.DataAvailable += OnData;
        _recorder.RecordingStopped += (_, e) => _chunks.Writer.TryComplete(e.Exception);
    }

    public static async Task<ProcessAudioCapture> StartAsync(uint processId, string? recordPath)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
            throw new PlatformNotSupportedException("A captura de som por programa precisa do Windows 11 ou do Windows 10 build 20348 ou mais nova.");
        var recorder = await new WasapiRecorderBuilder()
            .WithProcessLoopback(processId, ProcessLoopbackMode.IncludeTargetProcessTree)
            .WithBufferLength(40)
            .BuildAsync();
        var capture = new ProcessAudioCapture(recorder, recordPath);
        recorder.StartRecording();
        return capture;
    }

    public WaveFormat Format => _recorder.WaveFormat;
    public ChannelReader<CapturedChunk> Chunks => _chunks.Reader;
    public TimeSpan Captured => TimeSpan.FromSeconds((double)Interlocked.Read(ref _samples) / SpeechAudio.SampleRate);
    public long SilentPackets => Interlocked.Read(ref _silentPackets);

    /// <summary>Pacotes que o Windows marcou como descontinuidade (o programa demorou e perdeu áudio).</summary>
    public long Glitches => Interlocked.Read(ref _glitches);

    public TimeSpan? RecordingDuration => _recording?.Duration;

    private void OnData(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        // O QPC do pacote vem em unidades de 100 ns; o Stopwatch do Windows usa o mesmo relógio, em outra escala.
        var capturedAt = qpcPosition > 0 ? (long)(qpcPosition * (Stopwatch.Frequency / 1e7)) : Stopwatch.GetTimestamp();
        if (flags.HasFlag(AudioClientBufferFlags.Silent)) Interlocked.Increment(ref _silentPackets);
        if (flags.HasFlag(AudioClientBufferFlags.DataDiscontinuity)) Interlocked.Increment(ref _glitches);
        if (buffer.Length == 0) return;

        var interleaved = MemoryMarshal.Cast<byte, float>(buffer);
        _recording?.Write(interleaved);
        var mono = AudioMath.ToMono(interleaved, _channels);
        var samples = _resampler.Process(mono);
        if (samples.Length == 0) return;
        var first = Interlocked.Add(ref _samples, samples.Length) - samples.Length;
        _chunks.Writer.TryWrite(new CapturedChunk(samples, first, capturedAt));
    }

    public async ValueTask DisposeAsync()
    {
        _recorder.DataAvailable -= OnData;
        if (_recorder.CaptureState != CaptureState.Stopped) _recorder.StopRecording();
        await _recorder.DisposeAsync();
        _chunks.Writer.TryComplete();
        _recording?.Dispose();
    }
}
