using MpGameDub.Core.Pipeline;
using MpGameDub.Core.Speech;

namespace MpGameDub.Tests.Pipeline;

public class LiveWorkerTests
{
    private readonly ManualClock _clock = new();

    /// <summary>Fala de 1 s que termina em endSeconds no jogo; o detector confirma 500 ms depois.</summary>
    private static DetectedSpeech Speech(double endSeconds)
    {
        var end = (long)(endSeconds * 16000);
        var segment = new SpeechSegment(new float[16000], end - 16000, end + 3200, end, end + 8000, ForcedCut: false);
        var endAt = (long)(endSeconds * 1000);
        return new DetectedSpeech(segment, endAt, endAt + 500);
    }

    private UtteranceProcessor Processor(double asrMs = 0, bool speak = true) => new(
        new EngineSet(new FakeRecognizer(_ => { _clock.Advance(asrMs); return "Get down!"; }), new FakeTranslator(),
            speak ? new FakeSynthesizer(TimeSpan.FromSeconds(1)) : null),
        TimeSpan.FromSeconds(15));

    private static async Task Drain(LiveWorker worker, params DetectedSpeech[] speech)
    {
        foreach (var s in speech) worker.Add(s);
        worker.Complete();
        await worker.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Fala_dublada_mede_do_fim_da_fala_ate_a_voz_comecar()
    {
        // A fala acaba em 10 s, o detector confirma em 10,5 s, o motor leva 0,8 s e a saída tem 60 ms de latência.
        var output = new FakeOutput(_clock, latencyMs: 60);
        var worker = new LiveWorker(Processor(asrMs: 800), output, new StalePolicyOptions(), _clock);
        var shown = new List<UtteranceTrace>();
        worker.Finished += shown.Add;
        _clock.Now = 10_500;

        await Drain(worker, Speech(10));

        var t = Assert.Single(worker.Traces);
        Assert.Null(t.Dropped);
        Assert.Equal("[pt] Get down!", t.Translation);
        Assert.Equal(500, t.VadMs, 3);
        Assert.Equal(1360, t.EndToVoiceMs!.Value, 3);
        Assert.Equal(60, t.QueueMs, 3);
        Assert.Equal(TimeSpan.FromSeconds(11.36), t.VoiceStart);
        Assert.Equal([11_360L], output.Starts);
        Assert.True(worker.Voices.ContainsKey(t.Id));
        Assert.Equal([t], shown);
    }

    [Fact]
    public async Task Fala_com_outra_mais_nova_esperando_e_atropelada()
    {
        // O caso que derrubou o modo ao vivo: o motor pergunta à fila se há fala esperando, em toda fala.
        var output = new FakeOutput(_clock);
        var worker = new LiveWorker(Processor(), output, new StalePolicyOptions(), _clock);
        _clock.Now = 3_000;

        await Drain(worker, Speech(1.5), Speech(2.5));

        var t = worker.Traces;
        Assert.Equal([1, 2], t.Select(x => x.Id));
        Assert.Equal(StalePolicy.Overtaken, t[0].Dropped);
        Assert.Null(t[1].Dropped);
        Assert.Single(output.Starts);
        Assert.Equal([2], worker.Voices.Keys);
    }

    [Fact]
    public async Task Sem_regra_de_atropelo_as_duas_tocam_em_fila()
    {
        var output = new FakeOutput(_clock);
        var worker = new LiveWorker(Processor(), output, new StalePolicyOptions { KeepOnlyNewest = false }, _clock);
        _clock.Now = 3_000;

        await Drain(worker, Speech(1.5), Speech(2.5));

        Assert.All(worker.Traces, t => Assert.Null(t.Dropped));
        Assert.Equal([3_000L, 4_000L], output.Starts); // a segunda espera a primeira (1 s de voz) acabar
        Assert.Equal(1_000, worker.Traces[1].QueueMs, 3);
    }

    [Fact]
    public async Task Fala_que_ja_chega_velha_demais_nem_vai_para_o_motor()
    {
        var recognizer = new FakeRecognizer(_ => "Hello");
        var worker = new LiveWorker(new UtteranceProcessor(new EngineSet(recognizer, null, null), TimeSpan.FromSeconds(15)),
            new FakeOutput(_clock), new StalePolicyOptions { MaxAge = TimeSpan.FromSeconds(5) }, _clock);
        _clock.Now = 16_000; // a fala acabou em 10 s

        await Drain(worker, Speech(10));

        Assert.Equal(StalePolicy.TooOld, Assert.Single(worker.Traces).Dropped);
        Assert.Equal(0, recognizer.Calls);
    }

    [Fact]
    public async Task Voz_que_so_tocaria_depois_do_prazo_e_descartada_sem_ir_para_a_saida()
    {
        var output = new FakeOutput(_clock) { BusyUntil = 30_000 }; // alto-falante ocupado por muito tempo
        var worker = new LiveWorker(Processor(), output, new StalePolicyOptions { MaxAge = TimeSpan.FromSeconds(5) }, _clock);
        _clock.Now = 10_500;

        await Drain(worker, Speech(10));

        var t = Assert.Single(worker.Traces);
        Assert.Equal(StalePolicy.TooOld, t.Dropped);
        Assert.Equal("Get down!", t.Original);
        Assert.Null(t.EndToVoiceMs);
        Assert.Empty(output.Starts);
        Assert.Empty(worker.Voices);
    }

    [Fact]
    public async Task So_legenda_traduz_e_nao_precisa_de_saida_de_som()
    {
        var worker = new LiveWorker(Processor(asrMs: 300, speak: false), output: null, new StalePolicyOptions(), _clock);
        _clock.Now = 10_700;

        await Drain(worker, Speech(10));

        var t = Assert.Single(worker.Traces);
        Assert.Null(t.Dropped);
        Assert.Equal("[pt] Get down!", t.Translation);
        Assert.Null(t.EndToVoiceMs);
        Assert.Equal(200, t.QueueMs, 3); // confirmada em 10,5 s, o motor pegou em 10,7 s
    }

    [Fact]
    public async Task Parar_no_meio_cancela_a_fala_que_esta_no_motor()
    {
        using var cts = new CancellationTokenSource();
        var slow = new FakeRecognizer(_ => "Hello", TimeSpan.FromMinutes(1));
        var worker = new LiveWorker(new UtteranceProcessor(new EngineSet(slow, null, null), TimeSpan.FromMinutes(5)), null, new StalePolicyOptions(), TimeProvider.System);
        worker.Add(new DetectedSpeech(Speech(1).Segment, TimeProvider.System.GetTimestamp(), TimeProvider.System.GetTimestamp()));

        var run = worker.RunAsync(cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(1, slow.Calls);
    }

    /// <summary>Relógio que só anda quando o teste manda, em milissegundos.</summary>
    private sealed class ManualClock : TimeProvider
    {
        public long Now { get; set; }
        public void Advance(double ms) => Now += (long)ms;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => 1000;
    }

    /// <summary>Saída que toca uma voz atrás da outra, como o VoicePlayer, no relógio do teste.</summary>
    private sealed class FakeOutput(ManualClock clock, long latencyMs = 0) : IVoiceOutput
    {
        public long BusyUntil { get; set; }
        public List<long> Starts { get; } = [];
        public long NextStartAt => Math.Max(clock.Now, BusyUntil) + latencyMs;

        public long Enqueue(SynthesizedSpeech speech)
        {
            var start = NextStartAt;
            BusyUntil = start + (long)speech.Duration.TotalMilliseconds;
            Starts.Add(start);
            return start;
        }
    }
}
