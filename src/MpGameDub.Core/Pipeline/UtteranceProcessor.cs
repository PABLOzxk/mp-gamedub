using System.Diagnostics;
using MpGameDub.Core.Speech;
using MpGameDub.Core.Text;

namespace MpGameDub.Core.Pipeline;

/// <summary>Os motores de uma rodada. Sem tradutor, a legenda sai em inglês; sem voz, só legenda.</summary>
public sealed record EngineSet(ISpeechRecognizer Recognizer, ITranslator? Translator, ISpeechSynthesizer? Synthesizer)
{
    public string Describe() =>
        string.Join(" + ", new[] { Recognizer.Name, Translator?.Name, Synthesizer?.Name }.Where(n => n is not null));
}

/// <summary>
/// Leva uma fala detectada pelo reconhecimento, filtro, tradução e voz, medindo cada etapa.
/// Cada etapa tem prazo: se estourar, a fala é descartada com o motivo e o próximo passo nem começa.
/// </summary>
public sealed class UtteranceProcessor(EngineSet engines, TimeSpan stageTimeout)
{
    public const string TimedOut = "tempo esgotado";

    public EngineSet Engines { get; } = engines;

    /// <summary>Preenche os tempos e textos do trace e devolve a voz, ou null se a fala foi descartada.</summary>
    public async Task<SynthesizedSpeech?> ProcessAsync(UtteranceTrace trace, float[] samples16k, CancellationToken ct)
    {
        var transcript = await RunStage("no reconhecimento", trace, ms => trace.AsrMs = ms,
            c => Engines.Recognizer.RecognizeAsync(samples16k, c), ct);
        if (transcript is null) return null;

        var filtered = TranscriptFilter.Apply(transcript.Text);
        trace.Original = filtered.Text;
        if (!filtered.Accepted)
        {
            trace.Dropped = $"filtrada: {filtered.Reason}";
            return null;
        }

        if (Engines.Translator is { } translator)
        {
            var translation = await RunStage("na tradução", trace, ms => trace.MtMs = ms,
                c => translator.TranslateAsync(filtered.Text, c), ct);
            if (translation is null) return null;
            trace.Translation = translation.Trim();
        }
        else
        {
            trace.Translation = filtered.Text;
        }

        if (Engines.Synthesizer is not { } synthesizer || trace.Translation.Length == 0) return null;
        var voice = await RunStage("na voz", trace, ms => trace.TtsMs = ms,
            c => synthesizer.SynthesizeAsync(trace.Translation, c), ct);
        if (voice is not null) trace.VoiceDuration = voice.Duration;
        return voice;
    }

    private async Task<T?> RunStage<T>(string stage, UtteranceTrace trace, Action<double> record,
        Func<CancellationToken, Task<T>> run, CancellationToken ct) where T : class
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(stageTimeout);
        var watch = Stopwatch.StartNew();
        try
        {
            var result = await run(timeout.Token);
            record(watch.Elapsed.TotalMilliseconds);
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            record(watch.Elapsed.TotalMilliseconds);
            trace.Dropped = $"{TimedOut} {stage} ({stageTimeout.TotalSeconds:0} s)";
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            record(watch.Elapsed.TotalMilliseconds);
            trace.Dropped = $"erro {stage}: {ex.Message}";
            return null;
        }
    }
}
