using MpGameDub.Core.Pipeline;

namespace MpGameDub.Tests.Pipeline;

public class UtteranceProcessorTests
{
    private static UtteranceTrace NewTrace() => new() { Id = 1, SpeechStart = TimeSpan.Zero, SpeechEnd = TimeSpan.FromSeconds(1) };

    [Fact]
    public async Task Fala_boa_passa_por_todas_as_etapas()
    {
        var voice = new FakeSynthesizer(TimeSpan.FromSeconds(2));
        var processor = new UtteranceProcessor(new EngineSet(new FakeRecognizer(_ => " Get down! "), new FakeTranslator(), voice), TimeSpan.FromSeconds(5));
        var trace = NewTrace();

        var speech = await processor.ProcessAsync(trace, new float[16000], CancellationToken.None);

        Assert.NotNull(speech);
        Assert.Null(trace.Dropped);
        Assert.Equal("Get down!", trace.Original);
        Assert.Equal("[pt] Get down!", trace.Translation);
        Assert.Equal(["[pt] Get down!"], voice.Spoken);
        Assert.Equal(TimeSpan.FromSeconds(2), trace.VoiceDuration);
        Assert.True(trace.AsrMs >= 0 && trace.MtMs >= 0 && trace.TtsMs >= 0);
    }

    [Fact]
    public async Task Alucinacao_e_descartada_antes_de_traduzir()
    {
        var voice = new FakeSynthesizer(TimeSpan.FromSeconds(1));
        var processor = new UtteranceProcessor(new EngineSet(new FakeRecognizer(_ => "Thanks for watching!"), new FakeTranslator(), voice), TimeSpan.FromSeconds(5));
        var trace = NewTrace();

        Assert.Null(await processor.ProcessAsync(trace, new float[16000], CancellationToken.None));
        Assert.StartsWith("filtrada", trace.Dropped);
        Assert.Empty(voice.Spoken);
        Assert.Equal(0, trace.MtMs);
    }

    [Fact]
    public async Task Etapa_que_estoura_o_prazo_descarta_a_fala_com_o_motivo()
    {
        var processor = new UtteranceProcessor(
            new EngineSet(new FakeRecognizer(_ => "Hello", TimeSpan.FromSeconds(10)), new FakeTranslator(), null),
            TimeSpan.FromMilliseconds(100));
        var trace = NewTrace();

        Assert.Null(await processor.ProcessAsync(trace, new float[16000], CancellationToken.None));
        Assert.StartsWith(UtteranceProcessor.TimedOut + " no reconhecimento", trace.Dropped);
        Assert.InRange(trace.AsrMs, 50, 5000);
    }

    [Fact]
    public async Task Erro_do_motor_vira_descarte_e_nao_derruba_a_rodada()
    {
        var processor = new UtteranceProcessor(
            new EngineSet(new FakeRecognizer(_ => "Hello"), new FakeTranslator(error: new InvalidOperationException("sem memória")), null),
            TimeSpan.FromSeconds(5));
        var trace = NewTrace();

        Assert.Null(await processor.ProcessAsync(trace, new float[16000], CancellationToken.None));
        Assert.Equal("erro na tradução: sem memória", trace.Dropped);
    }

    [Fact]
    public async Task Cancelamento_de_quem_chamou_nao_vira_descarte()
    {
        var processor = new UtteranceProcessor(
            new EngineSet(new FakeRecognizer(_ => "Hello", TimeSpan.FromSeconds(10)), null, null), TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.ProcessAsync(NewTrace(), new float[16000], cts.Token));
    }

    [Fact]
    public async Task Sem_tradutor_e_sem_voz_vira_legenda_em_ingles()
    {
        var processor = new UtteranceProcessor(new EngineSet(new FakeRecognizer(_ => "Reload!"), null, null), TimeSpan.FromSeconds(5));
        var trace = NewTrace();

        Assert.Null(await processor.ProcessAsync(trace, new float[16000], CancellationToken.None));
        Assert.Null(trace.Dropped);
        Assert.Equal("Reload!", trace.Translation);
    }
}
