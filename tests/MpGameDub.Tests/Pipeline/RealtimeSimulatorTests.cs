using MpGameDub.Core.Pipeline;

namespace MpGameDub.Tests.Pipeline;

public class RealtimeSimulatorTests
{
    private static UtteranceTrace Trace(int id, double endSeconds, double processMs, double voiceSeconds, double vadMs = 400) => new()
    {
        Id = id,
        SpeechStart = TimeSpan.FromSeconds(endSeconds - 1),
        SpeechEnd = TimeSpan.FromSeconds(endSeconds),
        VadMs = vadMs,
        AsrMs = processMs,
        VoiceDuration = TimeSpan.FromSeconds(voiceSeconds),
    };

    [Fact]
    public void SpacedLines_AllPlay_WithLatencyEqualToVadPlusProcessing()
    {
        List<UtteranceTrace> t = [Trace(1, 2, 800, 1.5), Trace(2, 10, 900, 1.5)];
        RealtimeSimulator.Run(t, new StalePolicyOptions());

        Assert.All(t, x => Assert.Null(x.Dropped));
        Assert.Equal(1200, t[0].EndToVoiceMs!.Value, 3);
        Assert.Equal(1300, t[1].EndToVoiceMs!.Value, 3);
        Assert.Equal(TimeSpan.FromSeconds(3.2), t[0].VoiceStart);
    }

    [Fact]
    public void BackToBackLines_OldOneIsDropped_WhenNewerIsAlreadyWaiting()
    {
        // A fala 1 demora 3 s no motor; quando o motor libera, a 2 e a 3 já estão na fila.
        List<UtteranceTrace> t = [Trace(1, 1, 3000, 2), Trace(2, 2, 1000, 2), Trace(3, 3, 1000, 2)];
        RealtimeSimulator.Run(t, new StalePolicyOptions());

        Assert.Null(t[0].Dropped);
        Assert.Equal(StalePolicy.Overtaken, t[1].Dropped);
        Assert.Null(t[2].Dropped);
    }

    [Fact]
    public void WithoutKeepOnlyNewest_DelayAccumulatesUntilMaxAge()
    {
        var options = new StalePolicyOptions { KeepOnlyNewest = false, MaxAge = TimeSpan.FromSeconds(4) };
        List<UtteranceTrace> t = [.. Enumerable.Range(1, 6).Select(i => Trace(i, i, 1800, 1))];
        RealtimeSimulator.Run(t, options);

        var played = t.Where(x => x.Dropped is null).ToList();
        var latencies = played.Select(x => x.EndToVoiceMs!.Value).ToList();
        Assert.Equal(latencies.OrderBy(x => x), latencies); // o atraso só cresce
        Assert.Contains(t, x => x.Dropped == StalePolicy.TooOld);
        Assert.All(played, x => Assert.True(x.EndToVoiceMs <= 4000));
    }

    [Fact]
    public void SpeakerBusy_PushesNextVoiceBack()
    {
        // Voz 1 dura 5 s; a 2 fica pronta antes de a 1 terminar e espera o alto-falante.
        List<UtteranceTrace> t = [Trace(1, 1, 500, 5), Trace(2, 3, 500, 1)];
        RealtimeSimulator.Run(t, new StalePolicyOptions { MaxAge = TimeSpan.FromSeconds(10) });

        Assert.Equal(t[0].VoiceStart + TimeSpan.FromSeconds(5), t[1].VoiceStart);
        Assert.True(t[1].QueueMs > 0);
    }

    [Fact]
    public void AlreadyFiltered_IsLeftAlone()
    {
        var filtered = Trace(1, 1, 500, 1);
        filtered.Dropped = "vazio";
        RealtimeSimulator.Run([filtered], new StalePolicyOptions());
        Assert.Equal("vazio", filtered.Dropped);
        Assert.Null(filtered.EndToVoiceMs);
    }
}
