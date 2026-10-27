using System.Diagnostics;
using System.Threading.Channels;
using MpGameDub.Core.Audio;
using MpGameDub.Core.Pipeline;
using MpGameDub.Core.Reports;
using MpGameDub.Core.Speech;
using MpGameDub.Engines;
using MpGameDub.Windows;

namespace MpGameDub.Teste;

/// <summary>
/// O teste de verdade: captura o som do jogo enquanto o Pablo joga, mostra a legenda, toca a voz por cima com o
/// jogo abaixado, e mede tudo com o relógio real, inclusive a disputa da placa com o jogo.
/// </summary>
internal sealed class LiveSession
{
    private sealed record Pending(SpeechSegment Segment, long SpeechEndAt, long ConfirmedAt);

    private readonly RunOptions _options;
    private readonly AppPaths _paths;
    private readonly AudioSessionInfo _game;
    private readonly List<UtteranceTrace> _traces = [];
    private readonly Dictionary<int, SynthesizedSpeech> _voices = [];
    private readonly Channel<Pending> _queue = Channel.CreateUnbounded<Pending>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private long _captureStart;

    private LiveSession(AudioSessionInfo game, RunOptions options, AppPaths paths)
    {
        _game = game;
        _options = options;
        _paths = paths;
    }

    public static async Task RunAsync(AudioSessionInfo game, RunOptions options, AppPaths paths)
    {
        if (!await EngineLoader.EnsureModelsAsync(EngineLoader.Required(options), paths, ask: true)) return;
        if (options.Record)
        {
            Ui.Line("Com --gravar, o som do jogo desta sessão fica salvo em gravacoes\\ para montar o dublado.wav depois.");
            if (!Ui.YesNo("Gravar o som do jogo?", true)) options = options with { Record = false };
        }
        await new LiveSession(game, options, paths).RunAsync();
    }

    private async Task RunAsync()
    {
        using var monitor = new ResourceMonitor();
        using var engines = EngineLoader.Load(_options, _paths);
        Ui.Info("Aquecendo os motores...");
        var warmup = await BatchRunner.WarmUpAsync(engines.Processor, CancellationToken.None);

        string? recording = null;
        if (_options.Record)
        {
            Directory.CreateDirectory(_paths.Recordings);
            recording = Path.Combine(_paths.Recordings, $"{DateTime.Now:yyyyMMdd-HHmmss}-{_game.ProcessName}-aovivo.wav");
        }

        using var cts = new CancellationTokenSource();
        using var volume = new GameVolume(_game.ProcessId, _paths.VolumeState);
        using var player = engines.Processor.Engines.Synthesizer is null ? null : new VoicePlayer(KokoroSynthesizer.SampleRate);
        var capture = await ProcessAudioCapture.StartAsync(_game.ProcessId, recording);
        _captureStart = Stopwatch.GetTimestamp();

        Ui.Line();
        Ui.Ok($"Ouvindo {_game.ProcessName}. Jogue normalmente; aperte Enter aqui para parar.");
        Ui.Info("A legenda aparece abaixo. Deixe esta janela visível num segundo monitor, se tiver.");
        Ui.Line();

        var stop = GameCommands.WaitForEnter();
        var detect = Task.Run(() => DetectAsync(capture, engines.Vad, cts.Token));
        var work = Task.Run(() => WorkAsync(engines.Processor, player, cts.Token));
        var duck = player is null ? Task.CompletedTask : Task.Run(() => DuckAsync(player, volume, cts.Token));
        var status = Task.Run(() => StatusAsync(capture, monitor, cts.Token));

        var finished = await Task.WhenAny(stop, detect, work);
        if (finished != stop && finished.Exception is { } failure) Ui.Error(failure.InnerException?.Message ?? failure.Message);
        await capture.DisposeAsync();
        cts.Cancel();
        await Quietly(Task.WhenAll(detect, work, duck, status));
        volume.Restore();
        var duration = capture.Captured;

        Ui.Line();
        Ui.Ok($"Parado. {_traces.Count} falas em {Ui.Clock(duration)}.");
        Ui.Line("Se você mediu o 1% low do jogo (FrameView ou benchmark do jogo), informe agora. Só tem o FPS médio? Use o médio nos dois.");
        var fpsOff = Ui.AskNumber("1% low com o MP GameDub FECHADO:");
        var fpsOn = fpsOff is null ? null : Ui.AskNumber("1% low com o MP GameDub LIGADO:");

        var folder = _paths.NewReportFolder($"{_game.ProcessName}-aovivo-{_options.ShortName}");
        var notes = new List<string>(engines.Notes)
        {
            "Ao vivo: tempos medidos com o relógio real, com o jogo disputando a placa e o processador.",
            $"O fim da fala é contado pela hora em que o Windows capturou o áudio; a voz começa quando entra na saída de som (latência da saída: {player?.OutputLatency.TotalMilliseconds ?? 0:0} ms incluída).",
        };
        if (capture.Glitches > 0) notes.Add($"O Windows perdeu {capture.Glitches} pedaço(s) de áudio na captura (programa atrasado).");
        if (monitor.GpuCountersProblem is { } problem) notes.Add($"Contadores da placa indisponíveis: {problem}");
        if (engines.Processor.Engines.Synthesizer is not null)
            notes.Add($"Volume do jogo durante a voz: {_options.GameGainWhileSpeaking:P0}. A captura pega o som do jogo já abaixado, o que pode piorar o reconhecimento de falas que se sobrepõem à voz.");

        ReportWriter.Write(folder, new ReportInput
        {
            Title = $"MP GameDub, Fase 0 ao vivo: {_game.ProcessName}",
            Mode = "ao vivo",
            Source = $"{_game.ProcessName} (processo {_game.ProcessId})",
            Engines = engines.Processor.Engines.Describe(),
            Traces = _traces,
            AudioDuration = duration,
            When = DateTimeOffset.Now,
            Environment = SystemInfo.Describe(),
            WarmupMs = warmup,
            Resources = monitor.Summarize(),
            Notes = notes,
            FpsLowOff = fpsOff,
            FpsLowOn = fpsOn,
        });
        await File.WriteAllTextAsync(Path.Combine(folder, "legendas.srt"), SubtitleWriter.Srt(_traces));
        await File.WriteAllLinesAsync(Path.Combine(folder, "motores.log"), EngineLoader.NativeLogLines);

        if (recording is not null && File.Exists(recording) && _voices.Count > 0)
        {
            Ui.Info("Montando o dublado.wav com a gravação...");
            var clip = WavFile.Read(recording);
            var placed = _traces.Where(t => t.VoiceStart is not null && _voices.ContainsKey(t.Id)).Select(t => (t.VoiceStart!.Value, _voices[t.Id]));
            var mixed = DubbedMixer.Mix(clip, placed, new DuckingOptions { GameGain = 1f }); // o jogo já foi gravado abaixado
            WavFile.WritePcm16(Path.Combine(folder, "dublado.wav"), mixed.Samples, mixed.SampleRate, mixed.Channels);
        }
        Ui.Ok($"Relatório em {folder}");
    }

    /// <summary>Detector de fala no ritmo da captura. Nunca espera os motores: só enfileira.</summary>
    private async Task DetectAsync(ProcessAudioCapture capture, IVoiceActivityDetector vad, CancellationToken ct)
    {
        var slicer = new FrameSlicer(vad.FrameSize);
        var segmenter = new SpeechSegmenter(vad.FrameSize, new SegmenterOptions());
        var position = 0L;
        await foreach (var chunk in capture.Chunks.ReadAllAsync(ct))
        {
            // Hora (Stopwatch) da amostra "position" do áudio, a partir do pacote que a trouxe.
            long TimeOf(long sample) => chunk.CapturedAt + (long)((sample - chunk.FirstSample) / (double)SpeechAudio.SampleRate * Stopwatch.Frequency);

            foreach (var frame in slicer.Push(chunk.Samples))
            {
                position += frame.Length;
                if (segmenter.Push(frame, vad.Process(frame)) is not { } segment) continue;
                var confirmed = TimeOf(position);
                var voiceEnd = confirmed - (long)(segment.ConfirmationDelay.TotalSeconds * Stopwatch.Frequency);
                _queue.Writer.TryWrite(new Pending(segment, voiceEnd, confirmed));
            }
        }
        if (segmenter.Flush() is { } last)
        {
            var now = Stopwatch.GetTimestamp();
            _queue.Writer.TryWrite(new Pending(last, now, now));
        }
        _queue.Writer.TryComplete();
    }

    /// <summary>Um motor, uma fala por vez, com a regra de descarte para o atraso não acumular.</summary>
    private async Task WorkAsync(UtteranceProcessor processor, VoicePlayer? player, CancellationToken ct)
    {
        var stale = new StalePolicyOptions { MaxAge = _options.MaxAge };
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

            var start = Stopwatch.GetTimestamp();
            var reason = StalePolicy.DropReason(Stopwatch.GetElapsedTime(item.SpeechEndAt, start), _queue.Reader.Count > 0, stale);
            if (reason is not null)
            {
                trace.Dropped = reason;
                Show(trace, null);
                continue;
            }

            var voice = await processor.ProcessAsync(trace, item.Segment.Samples, ct);
            var ready = Stopwatch.GetTimestamp();
            if (voice is null || player is null)
            {
                trace.QueueMs = Ms(start - item.ConfirmedAt);
                Show(trace, null);
                continue;
            }

            if (Stopwatch.GetElapsedTime(item.SpeechEndAt, player.NextStartAt) > stale.MaxAge)
            {
                trace.Dropped = StalePolicy.TooOld;
                trace.QueueMs = Ms(start - item.ConfirmedAt);
                Show(trace, null);
                continue;
            }
            var (startsAt, _) = player.Enqueue(voice);
            trace.QueueMs = Ms(start - item.ConfirmedAt) + Ms(startsAt - ready);
            trace.EndToVoiceMs = Ms(startsAt - item.SpeechEndAt);
            trace.VoiceStart = trace.SpeechEnd + TimeSpan.FromMilliseconds(trace.EndToVoiceMs.Value);
            lock (_voices) _voices[trace.Id] = voice;
            Show(trace, trace.EndToVoiceMs);
        }
    }

    /// <summary>Abaixa o jogo enquanto há voz para tocar e devolve um pouco depois de ela acabar.</summary>
    private async Task DuckAsync(VoicePlayer player, GameVolume volume, CancellationToken ct)
    {
        var release = TimeSpan.FromMilliseconds(300);
        var lastVoice = 0L;
        var ducked = false;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(40));
        while (await timer.WaitForNextTickAsync(ct))
        {
            if (player.Pending > TimeSpan.Zero)
            {
                lastVoice = Stopwatch.GetTimestamp();
                if (!ducked) volume.Duck(_options.GameGainWhileSpeaking);
                ducked = true;
            }
            else if (ducked && Stopwatch.GetElapsedTime(lastVoice) > release)
            {
                volume.Restore();
                ducked = false;
            }
        }
    }

    private async Task StatusAsync(ProcessAudioCapture capture, ResourceMonitor monitor, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(ct))
        {
            int count;
            lock (_traces) count = _traces.Count;
            var last = monitor.Last;
            var vram = last?.VramMb is { } v ? v.ToString("0", Ui.Br) + " MB" : "?";
            Ui.Line($"  [{Ui.Clock(capture.Captured)}] falas {count} | memória da placa {vram} | CPU {last?.CpuPercent ?? 0:0}% | perdas de áudio {capture.Glitches}", ConsoleColor.DarkGray);
        }
    }

    private void Show(UtteranceTrace t, double? endToVoice)
    {
        var at = Ui.Clock(t.SpeechStart);
        if (t.Original.Length > 0) Ui.Line($"[{at}] EN  {t.Original}", ConsoleColor.Gray);
        if (t.Dropped is not null)
        {
            Ui.Line($"        --  {t.Dropped}", ConsoleColor.DarkYellow);
            return;
        }
        var delay = endToVoice is { } ms ? $"   ({Ui.Seconds(ms)})" : "";
        Ui.Line($"        PT  {t.Translation}{delay}", ConsoleColor.White);
    }

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    private static async Task Quietly(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
