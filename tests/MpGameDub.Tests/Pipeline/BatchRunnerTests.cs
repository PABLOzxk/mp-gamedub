using MpGameDub.Core.Pipeline;

namespace MpGameDub.Tests.Pipeline;

public class BatchRunnerTests
{
    [Fact]
    public async Task Acha_as_falas_mede_e_simula_a_linha_do_tempo()
    {
        var clip = TestAudio.WithSpeech(TimeSpan.FromSeconds(12), (1.0, 2.0), (5.0, 6.5), (9.0, 9.1));
        var recognizer = new FakeRecognizer(samples => $"line of {samples.Length / 16000.0:0.0} seconds");
        var voice = new FakeSynthesizer(TimeSpan.FromSeconds(1));
        var processor = new UtteranceProcessor(new EngineSet(recognizer, new FakeTranslator(), voice), TimeSpan.FromSeconds(5));

        var result = await BatchRunner.RunAsync(clip, new EnergyVad(), processor, new BatchOptions(), null, CancellationToken.None);

        // A terceira "fala" (100 ms) é curta demais e nem chega ao reconhecimento.
        Assert.Equal(2, result.Traces.Count);
        Assert.Equal(TimeSpan.FromSeconds(12), result.AudioDuration);
        Assert.Equal(2 + 1, recognizer.Calls); // + o aquecimento
        Assert.All(result.Traces, t => Assert.Null(t.Dropped));
        Assert.Equal(2, result.Voices.Count);

        var first = result.Traces[0];
        Assert.InRange(first.SpeechEnd.TotalSeconds, 1.95, 2.1);
        Assert.InRange(first.VadMs, 380, 450);
        Assert.NotNull(first.VoiceStart);
        Assert.True(first.VoiceStart > first.SpeechEnd);
        Assert.Equal(first.EndToVoiceMs!.Value, (first.VoiceStart!.Value - first.SpeechEnd).TotalMilliseconds, 3);
    }

    [Fact]
    public async Task Aquecimento_chama_cada_motor_uma_vez()
    {
        var recognizer = new FakeRecognizer(_ => "x");
        var voice = new FakeSynthesizer(TimeSpan.FromMilliseconds(100));
        var processor = new UtteranceProcessor(new EngineSet(recognizer, new FakeTranslator(), voice), TimeSpan.FromSeconds(5));

        var ms = await BatchRunner.WarmUpAsync(processor, CancellationToken.None);

        Assert.True(ms >= 0);
        Assert.Equal(1, recognizer.Calls);
        Assert.Single(voice.Spoken);
    }
}
