using MpGameDub.Core.Audio;
using MpGameDub.Core.Pipeline;
using MpGameDub.Core.Reports;
using MpGameDub.Windows;

namespace MpGameDub.Teste;

internal sealed record BatchSummary(string Engines, LatencySummary ShortEndToVoice, int Detected, int Voiced, double? PeakVramMb, double ProcessingShare, string Folder);

/// <summary>Processa uma gravação como se fosse ao vivo e escreve relatório, planilha, legenda e o áudio dublado.</summary>
internal static class BatchCommand
{
    public static async Task<BatchSummary?> RunAsync(string wavPath, RunOptions options, AppPaths paths, string mode = "gravação processada")
    {
        if (!File.Exists(wavPath))
        {
            Ui.Error($"arquivo não encontrado: {wavPath}");
            return null;
        }
        if (!await EngineLoader.EnsureModelsAsync(EngineLoader.Required(options), paths, ask: true)) return null;

        Ui.Info($"Lendo {Path.GetFileName(wavPath)}...");
        var clip = WavFile.Read(wavPath);
        Ui.Info($"{Ui.Clock(clip.Duration)} de áudio, {clip.SampleRate} Hz, {clip.Channels} canal(is).");

        using var monitor = new ResourceMonitor();
        using var engines = EngineLoader.Load(options, paths);
        var progress = new Progress<string>(text => Ui.Info("  " + text));
        var batch = new BatchOptions { Stale = new StalePolicyOptions { MaxAge = options.MaxAge } };
        var result = await BatchRunner.RunAsync(clip, engines.Vad, engines.Processor, batch, progress, CancellationToken.None);
        var resources = monitor.Summarize();

        var folder = paths.NewReportFolder($"{Path.GetFileNameWithoutExtension(wavPath)}-{options.ShortName}");
        var notes = new List<string>(engines.Notes)
        {
            "Gravação processada: os motores não disputaram a placa com o jogo, então os tempos são o melhor caso. O modo ao vivo mede o real.",
            $"Carregar os motores levou {Ui.Seconds(engines.LoadMs)}.",
            $"O detector de fala gastou {result.VadComputeMsPerSecond.ToString("0.0", Ui.Br)} ms por segundo de áudio.",
        };
        if (monitor.GpuCountersProblem is { } problem) notes.Add($"Contadores da placa indisponíveis: {problem}");

        ReportWriter.Write(folder, new ReportInput
        {
            Title = "MP GameDub, Fase 0: " + Path.GetFileName(wavPath),
            Mode = mode,
            Source = Path.GetFileName(wavPath),
            Engines = engines.Processor.Engines.Describe(),
            Traces = result.Traces,
            AudioDuration = result.AudioDuration,
            When = DateTimeOffset.Now,
            Environment = SystemInfo.Describe(),
            WarmupMs = result.WarmupMs,
            Resources = resources,
            Notes = notes,
        });
        await File.WriteAllTextAsync(Path.Combine(folder, "legendas.srt"), SubtitleWriter.Srt(result.Traces));
        await File.WriteAllLinesAsync(Path.Combine(folder, "motores.log"), EngineLoader.NativeLogLines);

        if (result.Voices.Count > 0)
        {
            Ui.Info("Montando o áudio dublado...");
            var voices = result.Traces
                .Where(t => t.VoiceStart is not null && result.Voices.ContainsKey(t.Id))
                .Select(t => (t.VoiceStart!.Value, result.Voices[t.Id]));
            var mixed = DubbedMixer.Mix(clip, voices, new DuckingOptions { GameGain = options.GameGainWhileSpeaking });
            WavFile.WritePcm16(Path.Combine(folder, "dublado.wav"), mixed.Samples, mixed.SampleRate, mixed.Channels);
        }

        var voiced = result.Traces.Where(t => t.EndToVoiceMs is not null).ToList();
        var shortOnes = LatencyStats.Summarize(voiced.Where(t => t.SpeechEnd - t.SpeechStart <= ReportWriter.ShortUtterance).Select(t => t.EndToVoiceMs!.Value));
        var share = result.AudioDuration.TotalMilliseconds > 0 ? result.Traces.Sum(t => t.ProcessingMs) / result.AudioDuration.TotalMilliseconds : 0;

        Ui.Line();
        Ui.Ok($"Pronto: {result.Traces.Count} falas detectadas, {voiced.Count} dubladas.");
        if (shortOnes.Count > 0)
            Ui.Line($"Fim da fala até a voz, falas curtas: metade em até {Ui.Seconds(shortOnes.P50)}, 95% em até {Ui.Seconds(shortOnes.P95)} (meta: {Ui.Seconds(ReportWriter.TargetP95Ms)}).");
        Ui.Line($"Relatório em {folder}");
        return new BatchSummary(engines.Processor.Engines.Describe(), shortOnes, result.Traces.Count, voiced.Count, resources.PeakVramMb, share, folder);
    }

    /// <summary>Roda a mesma gravação com cada Whisper e junta os números numa tabela.</summary>
    public static async Task CompareAsync(string wavPath, RunOptions options, AppPaths paths)
    {
        var all = RunOptions.WhisperChoices.Values.Select(w => options with { Whisper = w }).ToList();
        var models = all.SelectMany(EngineLoader.Required).Distinct().ToList();
        if (!await EngineLoader.EnsureModelsAsync(models, paths, ask: true)) return;

        var rows = new List<BatchSummary>();
        foreach (var run in all)
        {
            Ui.Title($"Comparando: {run.Whisper.Description}");
            if (await RunAsync(wavPath, run, paths, "comparação de modelos") is { } summary) rows.Add(summary);
        }
        if (rows.Count == 0) return;

        var md = new System.Text.StringBuilder();
        md.AppendLine($"# Comparação de modelos: {Path.GetFileName(wavPath)}");
        md.AppendLine();
        md.AppendLine("| Motores | Falas | Dubladas | Curtas p50 | Curtas p95 | Memória da placa | Tempo de motor |");
        md.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var r in rows)
            md.AppendLine($"| {r.Engines} | {r.Detected} | {r.Voiced} | {Ui.Seconds(r.ShortEndToVoice.P50)} | {Ui.Seconds(r.ShortEndToVoice.P95)} | {(r.PeakVramMb is { } v ? v.ToString("0", Ui.Br) + " MB" : "?")} | {(r.ProcessingShare * 100).ToString("0", Ui.Br)}% |");
        md.AppendLine();
        md.AppendLine("Cada linha tem a pasta própria com relatório, falas.csv e dublado.wav:");
        foreach (var r in rows) md.AppendLine($"- {Path.GetFileName(r.Folder)}");
        var file = Path.Combine(paths.Reports, $"{DateTime.Now:yyyyMMdd-HHmmss}-comparacao.md");
        await File.WriteAllTextAsync(file, md.ToString());
        Ui.Ok($"Comparação em {file}");
    }
}
