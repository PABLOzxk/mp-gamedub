namespace MpGameDub.Core.Pipeline;

/// <summary>
/// Reconstrói como um arquivo processado de uma vez teria soado ao vivo: um motor que faz uma fala por vez,
/// um alto-falante que toca uma voz por vez, e a regra de descarte decidindo o que ainda vale a pena.
/// Usa os tempos reais medidos de cada etapa. Falas já filtradas (Dropped preenchido) são ignoradas.
/// </summary>
public static class RealtimeSimulator
{
    public static void Run(IReadOnlyList<UtteranceTrace> traces, StalePolicyOptions options)
    {
        var pending = traces.Where(t => t.Dropped is null).OrderBy(Confirmed).ToList();
        var engineFree = TimeSpan.Zero;
        var speakerFree = TimeSpan.Zero;

        for (var i = 0; i < pending.Count; i++)
        {
            var trace = pending[i];
            var start = Max(Confirmed(trace), engineFree);
            var newerWaiting = i + 1 < pending.Count && Confirmed(pending[i + 1]) <= start;
            var reason = StalePolicy.DropReason(start - trace.SpeechEnd, newerWaiting, options);
            if (reason is not null)
            {
                trace.Dropped = reason;
                continue;
            }

            var ready = start + TimeSpan.FromMilliseconds(trace.ProcessingMs);
            engineFree = ready;
            var play = Max(ready, speakerFree);
            if (play - trace.SpeechEnd > options.MaxAge)
            {
                trace.Dropped = StalePolicy.TooOld;
                continue;
            }

            var voice = trace.VoiceDuration ?? TimeSpan.Zero;
            speakerFree = play + voice;
            trace.QueueMs = (start - Confirmed(trace) + (play - ready)).TotalMilliseconds;
            trace.VoiceStart = play;
            trace.EndToVoiceMs = (play - trace.SpeechEnd).TotalMilliseconds;
        }
    }

    private static TimeSpan Confirmed(UtteranceTrace t) => t.SpeechEnd + TimeSpan.FromMilliseconds(t.VadMs);

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
