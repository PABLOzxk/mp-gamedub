using System.Diagnostics;
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
    private readonly RunOptions _options;
    private readonly AppPaths _paths;
    private readonly AudioSessionInfo _game;

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
        using var engines = await EngineLoader.LoadAsync(_options, _paths);
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
        // TimeProvider.System conta no mesmo Stopwatch que a captura e a saída de som usam.
        var worker = new LiveWorker(engines.Processor, player, new StalePolicyOptions { MaxAge = _options.MaxAge }, TimeProvider.System);
        worker.Finished += Show;
        var capture = await ProcessAudioCapture.StartAsync(_game.ProcessId, recording);

        Ui.Line();
        Ui.Ok($"Ouvindo {_game.ProcessName}. Jogue normalmente; aperte Enter aqui para parar.");
        Ui.Info("A legenda aparece abaixo. Deixe esta janela visível num segundo monitor, se tiver.");
        Ui.Line();

        var stop = GameCommands.WaitForEnter();
        var detect = Task.Run(() => DetectAsync(capture, engines.Vad, worker, cts.Token));
        var work = Task.Run(() => worker.RunAsync(cts.Token));
        var duck = player is null ? Task.CompletedTask : Task.Run(() => DuckAsync(player, volume, cts.Token));
        var status = Task.Run(() => StatusAsync(capture, monitor, worker, cts.Token));
        // A captura fica presa ao processo escolhido: se o jogo fechar, ela só recebe silêncio, sem erro nenhum.
        // Sem o nome do processo (a lista mostra "processo N"), não dá para acompanhar, e a sessão só para no Enter.
        var gameGone = GameExit.IsRunning((int)_game.ProcessId, _game.ProcessName)
            ? GameExit.WaitAsync((int)_game.ProcessId, _game.ProcessName, TimeSpan.FromSeconds(2), cts.Token)
            : Task.Delay(Timeout.Infinite, cts.Token);

        await Task.WhenAny(stop, detect, work, gameGone);
        var gameClosed = gameGone.IsCompletedSuccessfully;
        var closing = capture.DisposeAsync().AsTask();
        cts.Cancel();
        var failure = await FirstFailure(work, detect, duck, status, gameGone, closing);
        volume.Restore();
        var duration = capture.Captured;
        var traces = worker.Traces;
        var voices = worker.Voices;

        Ui.Line();
        if (failure is not null)
        {
            var log = ErrorLog.Write(_paths, failure);
            Ui.Error($"o modo ao vivo parou: {failure.Message}");
            Ui.Line($"Os detalhes ficaram em {log}. O relatório abaixo tem o que deu para medir até aí.");
        }
        else if (gameClosed)
        {
            Ui.Warn($"o {_game.ProcessName} fechou, e a sessão parou junto. Para continuar, abra o jogo de novo e comece outra sessão ao vivo.");
        }
        else if (!stop.IsCompleted)
        {
            Ui.Warn("a captura do som do jogo terminou sozinha. O jogo fechou?");
        }
        if (!stop.IsCompleted)
        {
            // A espera do Enter continua lendo o teclado; sem isto, ela comeria as respostas das perguntas abaixo.
            Ui.Info("Aperte Enter para continuar.");
            await stop;
        }
        Ui.Ok($"Parado. {traces.Count} falas em {Ui.Clock(duration)}.");
        Ui.Line("Se você mediu o 1% low do jogo (FrameView ou benchmark do jogo), informe agora. Só tem o FPS médio? Use o médio nos dois.");
        var fpsOff = Ui.AskNumber("1% low com o MP GameDub FECHADO:");
        var fpsOn = fpsOff is null ? null : Ui.AskNumber("1% low com o MP GameDub LIGADO:");

        var folder = _paths.NewReportFolder($"{_game.ProcessName}-aovivo-{_options.ShortName}");
        var notes = new List<string>(engines.Notes)
        {
            "Ao vivo: tempos medidos com o relógio real, com o jogo disputando a placa e o processador.",
            $"O fim da fala é contado pela hora em que o Windows capturou o áudio; a voz começa quando entra na saída de som (latência da saída: {player?.OutputLatency.TotalMilliseconds ?? 0:0} ms incluída).",
        };
        if (failure is not null) notes.Add($"A sessão parou antes da hora por um erro: {failure.Message}");
        if (gameClosed) notes.Add($"O jogo fechou em {Ui.Clock(duration)} de áudio, e a sessão parou junto.");
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
            Traces = traces,
            AudioDuration = duration,
            When = DateTimeOffset.Now,
            Environment = SystemInfo.Describe(),
            WarmupMs = warmup,
            Resources = monitor.Summarize(),
            Notes = notes,
            FpsLowOff = fpsOff,
            FpsLowOn = fpsOn,
        });
        await File.WriteAllTextAsync(Path.Combine(folder, "legendas.srt"), SubtitleWriter.Srt(traces));
        await File.WriteAllLinesAsync(Path.Combine(folder, "motores.log"), EngineLoader.NativeLogLines);

        if (recording is not null && File.Exists(recording) && voices.Count > 0)
        {
            Ui.Info("Montando o dublado.wav com a gravação...");
            var clip = WavFile.Read(recording);
            var placed = traces.Where(t => t.VoiceStart is not null && voices.ContainsKey(t.Id)).Select(t => (t.VoiceStart!.Value, voices[t.Id]));
            var mixed = DubbedMixer.Mix(clip, placed, new DuckingOptions { GameGain = 1f }); // o jogo já foi gravado abaixado
            WavFile.WritePcm16(Path.Combine(folder, "dublado.wav"), mixed.Samples, mixed.SampleRate, mixed.Channels);
        }
        Ui.Ok($"Relatório em {folder}");
    }

    /// <summary>Detector de fala no ritmo da captura. Nunca espera os motores: só enfileira.</summary>
    private static async Task DetectAsync(ProcessAudioCapture capture, IVoiceActivityDetector vad, LiveWorker worker, CancellationToken ct)
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
                worker.Add(new DetectedSpeech(segment, voiceEnd, confirmed));
            }
        }
        if (segmenter.Flush() is { } last)
        {
            var now = Stopwatch.GetTimestamp();
            worker.Add(new DetectedSpeech(last, now, now));
        }
        worker.Complete();
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

    private static async Task StatusAsync(ProcessAudioCapture capture, ResourceMonitor monitor, LiveWorker worker, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(ct))
        {
            var count = worker.Count;
            var last = monitor.Last;
            var vram = last?.VramMb is { } v ? v.ToString("0", Ui.Br) + " MB" : "?";
            Ui.Line($"  [{Ui.Clock(capture.Captured)}] falas {count} | memória da placa {vram} | CPU {last?.CpuPercent ?? 0:0}% | perdas de áudio {capture.Glitches}", ConsoleColor.DarkGray);
        }
    }

    private static void Show(UtteranceTrace t)
    {
        var at = Ui.Clock(t.SpeechStart);
        if (t.Original.Length > 0) Ui.Line($"[{at}] EN  {t.Original}", ConsoleColor.Gray);
        if (t.Dropped is not null)
        {
            Ui.Line($"        --  {t.Dropped}", ConsoleColor.DarkYellow);
            return;
        }
        var delay = t.EndToVoiceMs is { } ms ? $"   ({Ui.Seconds(ms)})" : "";
        Ui.Line($"        PT  {t.Translation}{delay}", ConsoleColor.White);
    }

    /// <summary>Espera todas e devolve o primeiro erro que não seja o cancelamento do fim da sessão.</summary>
    private static async Task<Exception?> FirstFailure(params Task[] tasks)
    {
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception)
        {
            // Os erros são lidos de cada tarefa abaixo; o WhenAll só devolveria o primeiro.
        }
        return tasks.Select(t => t.Exception?.InnerException).FirstOrDefault(e => e is not null and not OperationCanceledException);
    }
}
