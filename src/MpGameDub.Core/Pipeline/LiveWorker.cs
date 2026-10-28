using System.Threading.Channels;
using MpGameDub.Core.Speech;

namespace MpGameDub.Core.Pipeline;

/// <summary>Uma fala que o detector fechou. SpeechEndAt e ConfirmedAt são horas no relógio do LiveWorker.</summary>
public sealed record DetectedSpeech(SpeechSegment Segment, long SpeechEndAt, long ConfirmedAt);

/// <summary>A saída da voz traduzida, uma fala atrás da outra, com horas no relógio do LiveWorker.</summary>
public interface IVoiceOutput
{
    /// <summary>Quando uma fala posta na fila agora começaria a soar.</summary>
    long NextStartAt { get; }

    /// <summary>Põe a fala na fila e devolve quando ela vai começar a soar.</summary>
    long Enqueue(SynthesizedSpeech speech);
}

/// <summary>
/// O lado do motor no modo ao vivo: pega as falas do detector uma por vez, aplica a regra de descarte para o atraso
/// não acumular e põe a voz na saída. Mede com o relógio dado: o do Windows ao vivo, um de mentira nos testes.
/// </summary>
public sealed class LiveWorker(UtteranceProcessor processor, IVoiceOutput? output, StalePolicyOptions stale, TimeProvider clock)
{
    // Sem SingleReader de propósito: com ele o .NET monta uma fila que não sabe contar, e Reader.Count lança
    // NotSupportedException. Foi isso que derrubou o modo ao vivo na primeira fala do Bully.
    private readonly Channel<DetectedSpeech> _queue = Channel.CreateUnbounded<DetectedSpeech>(new UnboundedChannelOptions { SingleWriter = true });
    private readonly List<UtteranceTrace> _traces = [];
    private readonly Dictionary<int, SynthesizedSpeech> _voices = [];

    /// <summary>Chamado a cada fala terminada, dublada ou descartada, na thread do motor.</summary>
    public event Action<UtteranceTrace>? Finished;

    public void Add(DetectedSpeech speech) => _queue.Writer.TryWrite(speech);

    /// <summary>O detector acabou: o motor termina as falas que já estão na fila e sai.</summary>
    public void Complete() => _queue.Writer.TryComplete();

    public int Count
    {
        get { lock (_traces) return _traces.Count; }
    }

    public IReadOnlyList<UtteranceTrace> Traces
    {
        get { lock (_traces) return [.. _traces]; }
    }

    /// <summary>As vozes que tocaram, pelo Id da fala, para montar o dublado.wav.</summary>
    public IReadOnlyDictionary<int, SynthesizedSpeech> Voices
    {
        get { lock (_voices) return new Dictionary<int, SynthesizedSpeech>(_voices); }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var id = 0;
        await foreach (var item in _queue.Reader.ReadAllAsync(ct))
        {
            var trace = new UtteranceTrace
            {
                Id = ++id,
                SpeechStart = item.Segment.Start,
                SpeechEnd = item.Segment.VoiceEnd,
                ForcedCut = item.Segment.ForcedCut,
                VadMs = Ms(item.ConfirmedAt - item.SpeechEndAt),
            };
            lock (_traces) _traces.Add(trace);

            var start = clock.GetTimestamp();
            trace.Dropped = StalePolicy.DropReason(clock.GetElapsedTime(item.SpeechEndAt, start), _queue.Reader.Count > 0, stale);
            if (trace.Dropped is null) await DubAsync(trace, item, start, ct);
            Finished?.Invoke(trace);
        }
    }

    private async Task DubAsync(UtteranceTrace trace, DetectedSpeech item, long start, CancellationToken ct)
    {
        var voice = await processor.ProcessAsync(trace, item.Segment.Samples, ct);
        var ready = clock.GetTimestamp();
        trace.QueueMs = Ms(start - item.ConfirmedAt);
        if (voice is null || output is null) return;

        if (clock.GetElapsedTime(item.SpeechEndAt, output.NextStartAt) > stale.MaxAge)
        {
            trace.Dropped = StalePolicy.TooOld;
            return;
        }
        var startsAt = output.Enqueue(voice);
        trace.QueueMs += Ms(startsAt - ready);
        trace.EndToVoiceMs = Ms(startsAt - item.SpeechEndAt);
        trace.VoiceStart = trace.SpeechEnd + TimeSpan.FromMilliseconds(trace.EndToVoiceMs.Value);
        lock (_voices) _voices[trace.Id] = voice;
    }

    private double Ms(long ticks) => ticks * 1000.0 / clock.TimestampFrequency;
}
