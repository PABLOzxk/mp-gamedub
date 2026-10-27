using MpGameDub.Core.Pipeline;
using MpGameDub.Core.Reports;

namespace MpGameDub.Tests.Reports;

public class ReportWriterTests
{
    private static UtteranceTrace Trace(int id, double start, double end, double? endToVoice, string? dropped = null) => new()
    {
        Id = id,
        SpeechStart = TimeSpan.FromSeconds(start),
        SpeechEnd = TimeSpan.FromSeconds(end),
        VadMs = 416,
        AsrMs = 800.5,
        MtMs = 120,
        TtsMs = 600,
        EndToVoiceMs = endToVoice,
        VoiceStart = endToVoice is { } e ? TimeSpan.FromSeconds(end) + TimeSpan.FromMilliseconds(e) : null,
        Original = "Get down; now!",
        Translation = "Abaixa \"agora\"!",
        Dropped = dropped,
    };

    private static ReportInput Input(params UtteranceTrace[] traces) => new()
    {
        Title = "Teste",
        Mode = "gravação processada",
        Source = "jogo.wav",
        Engines = "Whisper + Opus-MT + Kokoro",
        Traces = traces,
        AudioDuration = TimeSpan.FromSeconds(60),
        When = new DateTimeOffset(2026, 10, 27, 15, 0, 0, TimeSpan.Zero),
        Resources = new ResourceSummary(1834, null, 22.4, 51),
    };

    [Fact]
    public void Meta_de_latencia_passa_ou_falha_pelo_p95_das_falas_curtas()
    {
        var ok = ReportWriter.Markdown(Input(Trace(1, 0, 2, 2000), Trace(2, 5, 7, 3000)));
        Assert.Contains("| sim |", ok);

        var slow = ReportWriter.Markdown(Input(Trace(1, 0, 2, 2000), Trace(2, 5, 7, 6000)));
        Assert.Contains("| não |", slow);

        // Fala longa (6 s) lenta não entra na meta das curtas.
        var longOnly = ReportWriter.Markdown(Input(Trace(1, 0, 2, 2000), Trace(2, 10, 16, 9000)));
        Assert.Contains("| sim |", longOnly);
    }

    [Fact]
    public void Conta_descartes_por_motivo_e_mostra_o_que_o_windows_nao_informou()
    {
        var md = ReportWriter.Markdown(Input(
            Trace(1, 0, 2, 2000),
            Trace(2, 3, 4, null, StalePolicy.TooOld),
            Trace(3, 5, 6, null, StalePolicy.TooOld),
            Trace(4, 7, 8, null, "erro na voz: falhou")));

        Assert.Contains("- Detectadas: 4", md);
        Assert.Contains("- Dubladas: 1", md);
        Assert.Contains("- Descartadas, velha demais: 2", md);
        Assert.Contains("- Descartadas, erro na voz: 1", md);
        Assert.Contains("1834 MB", md);
        Assert.Contains("Memória RAM (pico): o Windows não informou", md);
    }

    [Fact]
    public void Csv_usa_ponto_e_virgula_virgula_decimal_e_aspas()
    {
        var csv = ReportWriter.Csv([Trace(1, 0, 2.5, 1999.25)]);
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        Assert.StartsWith("fala;inicio_s;fim_s;", lines[0]);
        Assert.Contains(";2,5;", lines[1]);
        Assert.Contains(";800,5;", lines[1]);
        Assert.Contains("\"Get down; now!\"", lines[1]);
        Assert.Contains("\"Abaixa \"\"agora\"\"!\"", lines[1]);
    }

    [Fact]
    public void Grava_os_dois_arquivos()
    {
        var folder = Path.Combine(Path.GetTempPath(), "mpgd-relatorio-" + Guid.NewGuid());
        try
        {
            ReportWriter.Write(folder, Input(Trace(1, 0, 2, 2000)));
            Assert.True(File.Exists(Path.Combine(folder, "relatorio.md")));
            var bytes = File.ReadAllBytes(Path.Combine(folder, "falas.csv"));
            Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]); // BOM para o Excel acertar os acentos
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

public class FpsAndSubtitleTests
{
    private static ReportInput Input(double? off, double? on) => new()
    {
        Title = "t", Mode = "ao vivo", Source = "jogo", Engines = "e", Traces = [], AudioDuration = TimeSpan.FromMinutes(1),
        When = DateTimeOffset.UnixEpoch, FpsLowOff = off, FpsLowOn = on,
    };

    [Fact]
    public void Queda_de_fps_anotada_vira_resultado()
    {
        Assert.Contains("(5% de queda) | sim |", ReportWriter.Markdown(Input(60, 57)));
        Assert.Contains("(25% de queda) | não |", ReportWriter.Markdown(Input(60, 45)));
        Assert.Contains("| você mede |", ReportWriter.Markdown(Input(null, null)));
    }

    [Fact]
    public void Srt_usa_o_tempo_da_fala_e_pula_falas_sem_traducao()
    {
        var srt = SubtitleWriter.Srt([
            new UtteranceTrace { Id = 2, SpeechStart = TimeSpan.FromSeconds(65.5), SpeechEnd = TimeSpan.FromSeconds(65.8), Translation = "Recarregando!" },
            new UtteranceTrace { Id = 1, SpeechStart = TimeSpan.FromSeconds(1), SpeechEnd = TimeSpan.FromSeconds(3), Translation = "" },
        ]);
        Assert.Equal("1\r\n00:01:05,500 --> 00:01:06,500\r\nRecarregando!\r\n\r\n".Replace("\r\n", Environment.NewLine), srt);
    }
}
