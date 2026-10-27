using System.Globalization;
using System.Text;
using MpGameDub.Core.Pipeline;

namespace MpGameDub.Core.Reports;

/// <summary>Uso de máquina medido durante a rodada. Null quando o Windows não deu o número.</summary>
public sealed record ResourceSummary(double? PeakVramMb, double? PeakRamMb, double? AverageCpuPercent, double? AverageGpuPercent);

public sealed record ReportInput
{
    public required string Title { get; init; }
    public required string Mode { get; init; }
    public required string Source { get; init; }
    public required string Engines { get; init; }
    public required IReadOnlyList<UtteranceTrace> Traces { get; init; }
    public required TimeSpan AudioDuration { get; init; }
    public required DateTimeOffset When { get; init; }
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    public double? WarmupMs { get; init; }
    public ResourceSummary? Resources { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>1% low do jogo (FPS) anotado pelo usuário com a IA desligada e ligada, se ele anotou.</summary>
    public double? FpsLowOff { get; init; }
    public double? FpsLowOn { get; init; }
}

/// <summary>
/// Escreve relatorio.md (para ler) e falas.csv (para abrir no Excel: ponto e vírgula, vírgula decimal, UTF-8 com BOM).
/// As metas são as combinadas para a Fase 0; as que só dá para medir jogando ficam marcadas para o Pablo preencher.
/// </summary>
public static class ReportWriter
{
    /// <summary>Fala curta para a meta de latência: até 4 s de voz.</summary>
    public static readonly TimeSpan ShortUtterance = TimeSpan.FromSeconds(4);
    public const double TargetP95Ms = 3500;
    public const double MaxFpsDropPercent = 10;

    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    public static void Write(string folder, ReportInput input)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "relatorio.md"), Markdown(input), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(folder, "falas.csv"), Csv(input.Traces), new UTF8Encoding(true));
    }

    public static string Markdown(ReportInput input)
    {
        var traces = input.Traces;
        var voiced = traces.Where(t => t.EndToVoiceMs is not null).ToList();
        var shortVoiced = voiced.Where(t => t.SpeechEnd - t.SpeechStart <= ShortUtterance).ToList();
        var shortSummary = LatencyStats.Summarize(shortVoiced.Select(t => t.EndToVoiceMs!.Value));
        var md = new StringBuilder();

        md.AppendLine($"# {input.Title}");
        md.AppendLine();
        md.AppendLine($"- Quando: {input.When.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Br)}");
        md.AppendLine($"- Modo: {input.Mode}");
        md.AppendLine($"- Áudio: {input.Source} ({Clock(input.AudioDuration)})");
        md.AppendLine($"- Motores: {input.Engines}");
        foreach (var (key, value) in input.Environment) md.AppendLine($"- {key}: {value}");
        if (input.WarmupMs is { } warmup) md.AppendLine($"- Aquecimento dos motores (fora das medições): {Seconds(warmup)}");
        md.AppendLine();

        md.AppendLine("## Metas da Fase 0");
        md.AppendLine();
        md.AppendLine("| Meta | Medido | Passou? |");
        md.AppendLine("|---|---|---|");
        var p95 = shortSummary.Count > 0 ? Seconds(shortSummary.P95) : "sem falas curtas dubladas";
        var passed = shortSummary.Count == 0 ? "sem dados" : shortSummary.P95 <= TargetP95Ms ? "sim" : "não";
        md.AppendLine($"| Fim da fala até o início da voz, p95 das falas curtas (até {ShortUtterance.TotalSeconds:0} s), no máximo {Seconds(TargetP95Ms)} | {p95} em {shortSummary.Count} falas | {passed} |");
        md.AppendLine($"| Pelo menos 15 de 20 falas compreensíveis | ouça o dublado.wav e conte | você decide |");
        if (input.FpsLowOff is > 0 and var off && input.FpsLowOn is { } on)
        {
            var drop = (off - on) / off * 100;
            md.AppendLine($"| 1% low do jogo cai no máximo {MaxFpsDropPercent:0}% com a IA ligada | {off.ToString("0", Br)} → {on.ToString("0", Br)} FPS ({drop.ToString("0", Br)}% de queda) | {(drop <= MaxFpsDropPercent ? "sim" : "não")} |");
        }
        else
        {
            md.AppendLine($"| 1% low do jogo cai no máximo {MaxFpsDropPercent:0}% com a IA ligada | compare no contador de FPS do jogo ou do Steam | você mede |");
        }
        md.AppendLine($"| Voz aceitável para jogar uma hora | ouça o dublado.wav | você decide |");
        md.AppendLine();

        md.AppendLine("## Falas");
        md.AppendLine();
        md.AppendLine($"- Detectadas: {traces.Count}");
        md.AppendLine($"- Dubladas: {voiced.Count}");
        foreach (var group in traces.Where(t => t.Dropped is not null).GroupBy(t => Reason(t.Dropped!)).OrderByDescending(g => g.Count()))
            md.AppendLine($"- Descartadas, {group.Key}: {group.Count()}");
        var forced = traces.Count(t => t.ForcedCut);
        if (forced > 0) md.AppendLine($"- Cortadas no meio por serem longas demais: {forced}");
        md.AppendLine();

        md.AppendLine("## Tempos (segundos)");
        md.AppendLine();
        md.AppendLine("| Etapa | Falas | p50 | p95 | p99 | Pior |");
        md.AppendLine("|---|---|---|---|---|---|");
        Row(md, "Detector confirmar o fim da fala", traces.Select(t => t.VadMs));
        Row(md, "Reconhecimento (Whisper)", traces.Where(t => t.AsrMs > 0).Select(t => t.AsrMs));
        Row(md, "Tradução", traces.Where(t => t.MtMs > 0).Select(t => t.MtMs));
        Row(md, "Voz", traces.Where(t => t.TtsMs > 0).Select(t => t.TtsMs));
        Row(md, "Espera na fila", voiced.Select(t => t.QueueMs));
        Row(md, "Fim da fala até a voz, todas", voiced.Select(t => t.EndToVoiceMs!.Value));
        Row(md, "Fim da fala até a voz, curtas", shortVoiced.Select(t => t.EndToVoiceMs!.Value));
        md.AppendLine();
        var processing = traces.Sum(t => t.ProcessingMs);
        if (input.AudioDuration > TimeSpan.Zero)
            md.AppendLine($"Os motores trabalharam {Seconds(processing)} para {Clock(input.AudioDuration)} de áudio ({processing / input.AudioDuration.TotalMilliseconds * 100:0}% do tempo).");
        md.AppendLine();

        if (input.Resources is { } r)
        {
            md.AppendLine("## Uso da máquina pelo MP GameDub");
            md.AppendLine();
            md.AppendLine($"- Memória da placa (pico): {Megabytes(r.PeakVramMb)}");
            md.AppendLine($"- Memória RAM (pico): {Megabytes(r.PeakRamMb)}");
            md.AppendLine($"- Processador (média): {Percent(r.AverageCpuPercent)}");
            md.AppendLine($"- Placa de vídeo (média): {Percent(r.AverageGpuPercent)}");
            md.AppendLine();
        }

        if (input.Notes.Count > 0)
        {
            md.AppendLine("## Observações");
            md.AppendLine();
            foreach (var note in input.Notes) md.AppendLine($"- {note}");
            md.AppendLine();
        }

        md.AppendLine("## Cada fala");
        md.AppendLine();
        md.AppendLine("| # | Início | Original | Tradução | Fim até a voz | Descarte |");
        md.AppendLine("|---|---|---|---|---|---|");
        foreach (var t in traces)
            md.AppendLine($"| {t.Id} | {Clock(t.SpeechStart)} | {Cell(t.Original)} | {Cell(t.Translation)} | {(t.EndToVoiceMs is { } e ? Seconds(e) : "")} | {Cell(t.Dropped ?? "")} |");
        return md.ToString();
    }

    public static string Csv(IReadOnlyList<UtteranceTrace> traces)
    {
        var csv = new StringBuilder();
        csv.AppendLine("fala;inicio_s;fim_s;cortada;detector_ms;reconhecimento_ms;traducao_ms;voz_ms;fila_ms;fim_ate_voz_ms;voz_inicio_s;voz_duracao_s;original;traducao;descarte");
        foreach (var t in traces)
        {
            csv.AppendJoin(';',
                t.Id.ToString(Br),
                Number(t.SpeechStart.TotalSeconds),
                Number(t.SpeechEnd.TotalSeconds),
                t.ForcedCut ? "sim" : "não",
                Number(t.VadMs),
                Number(t.AsrMs),
                Number(t.MtMs),
                Number(t.TtsMs),
                Number(t.QueueMs),
                t.EndToVoiceMs is { } e ? Number(e) : "",
                t.VoiceStart is { } v ? Number(v.TotalSeconds) : "",
                t.VoiceDuration is { } d ? Number(d.TotalSeconds) : "",
                Quote(t.Original),
                Quote(t.Translation),
                Quote(t.Dropped ?? ""));
            csv.AppendLine();
        }
        return csv.ToString();
    }

    private static void Row(StringBuilder md, string label, IEnumerable<double> values)
    {
        var s = LatencyStats.Summarize(values);
        if (s.Count == 0)
        {
            md.AppendLine($"| {label} | 0 | | | | |");
            return;
        }
        md.AppendLine($"| {label} | {s.Count} | {Sec(s.P50)} | {Sec(s.P95)} | {Sec(s.P99)} | {Sec(s.Max)} |");
    }

    // "erro na voz: mensagem" e "tempo esgotado na tradução (15 s)" agrupam pelo começo.
    private static string Reason(string dropped)
    {
        var colon = dropped.IndexOf(':');
        return colon > 0 && !dropped.StartsWith("filtrada", StringComparison.Ordinal) ? dropped[..colon] : dropped;
    }

    private static string Sec(double ms) => (ms / 1000).ToString("0.00", Br);
    private static string Seconds(double ms) => Sec(ms) + " s";
    private static string Number(double value) => value.ToString("0.###", Br);
    private static string Clock(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss", Br) : t.ToString(@"m\:ss", Br);
    private static string Megabytes(double? mb) => mb is { } v ? $"{v.ToString("0", Br)} MB" : "o Windows não informou";
    private static string Percent(double? p) => p is { } v ? $"{v.ToString("0", Br)}%" : "o Windows não informou";
    private static string Cell(string text) => text.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    private static string Quote(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";
}
