using System.Diagnostics;
using MpGameDub.Core.Audio;
using MpGameDub.Core.Speech;

namespace MpGameDub.Core.Pipeline;

public sealed record BatchOptions
{
    public SegmenterOptions Segmenter { get; init; } = new();
    public StalePolicyOptions Stale { get; init; } = new();
    public TimeSpan StageTimeout { get; init; } = TimeSpan.FromSeconds(15);
}

public sealed record BatchResult(
    IReadOnlyList<UtteranceTrace> Traces,
    IReadOnlyDictionary<int, SynthesizedSpeech> Voices,
    TimeSpan AudioDuration,
    double VadComputeMsPerSecond,
    double WarmupMs);

/// <summary>
/// Processa uma gravação inteira como se fosse ao vivo: o detector anda quadro a quadro, cada fala passa pelos
/// motores com os tempos medidos de verdade, e o simulador decide o que teria tocado, quando, e o que seria descartado.
/// Os motores aqui não disputam a placa com o jogo, então o número é o melhor caso; o modo ao vivo mede o real.
/// </summary>
public static class BatchRunner
{
    public static async Task<BatchResult> RunAsync(AudioClip clip, IVoiceActivityDetector vad, UtteranceProcessor processor,
        BatchOptions options, IProgress<string>? progress, CancellationToken ct)
    {
        var audio = clip.ToMono().Resample(SpeechAudio.SampleRate).Samples;
        var warmup = await WarmUpAsync(processor, ct);

        progress?.Report("Procurando as falas...");
        vad.Reset();
        var segmenter = new SpeechSegmenter(vad.FrameSize, options.Segmenter);
        var segments = new List<SpeechSegment>();
        var vadWatch = Stopwatch.StartNew();
        for (var i = 0; i + vad.FrameSize <= audio.Length; i += vad.FrameSize)
        {
            ct.ThrowIfCancellationRequested();
            var frame = audio.AsSpan(i, vad.FrameSize);
            if (segmenter.Push(frame, vad.Process(frame)) is { } segment) segments.Add(segment);
        }
        if (segmenter.Flush() is { } last) segments.Add(last);
        var seconds = (double)audio.Length / SpeechAudio.SampleRate;
        var vadPerSecond = seconds > 0 ? vadWatch.Elapsed.TotalMilliseconds / seconds : 0;

        var traces = new List<UtteranceTrace>();
        var voices = new Dictionary<int, SynthesizedSpeech>();
        for (var i = 0; i < segments.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var segment = segments[i];
            var trace = new UtteranceTrace
            {
                Id = i + 1,
                SpeechStart = segment.Start,
                SpeechEnd = segment.VoiceEnd,
                ForcedCut = segment.ForcedCut,
                VadMs = segment.ConfirmationDelay.TotalMilliseconds,
            };
            progress?.Report($"Fala {i + 1} de {segments.Count} ({segment.Start:mm\\:ss})");
            if (await processor.ProcessAsync(trace, segment.Samples, ct) is { } voice) voices[trace.Id] = voice;
            traces.Add(trace);
        }

        RealtimeSimulator.Run(traces, options.Stale);
        return new BatchResult(traces, voices, TimeSpan.FromSeconds(seconds), vadPerSecond, warmup);
    }

    /// <summary>
    /// A primeira chamada de cada motor carrega pesos e compila kernels na placa; sem aquecer, a fala 1 sai com
    /// segundos a mais que nunca se repetem. O aquecimento é medido e relatado à parte.
    /// </summary>
    public static async Task<double> WarmUpAsync(UtteranceProcessor processor, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var silence = new float[SpeechAudio.SampleRate];
        await processor.Engines.Recognizer.RecognizeAsync(silence, ct);
        if (processor.Engines.Translator is { } translator) await translator.TranslateAsync("Let's go.", ct);
        if (processor.Engines.Synthesizer is { } synthesizer) await synthesizer.SynthesizeAsync("Vamos.", ct);
        return watch.Elapsed.TotalMilliseconds;
    }
}
