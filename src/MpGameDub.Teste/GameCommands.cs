using System.Diagnostics;
using MpGameDub.Windows;

namespace MpGameDub.Teste;

/// <summary>Escolher o jogo na lista de quem toca som, e gravar o áudio dele quando o usuário pedir.</summary>
internal static class GameCommands
{
    public static IReadOnlyList<AudioSessionInfo> ShowGames()
    {
        var sessions = AudioSessions.List();
        if (sessions.Count == 0)
        {
            Ui.Warn("nenhum programa está tocando som agora. Abra o jogo, deixe o som ligado e tente de novo.");
            return sessions;
        }
        Ui.Line("Programas com som agora (o jogo costuma ser o de nível mais alto):");
        for (var i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            var bar = new string('|', (int)Math.Round(s.Peak * 20));
            var title = s.Title.Length > 0 ? $" \"{Cut(s.Title, 40)}\"" : "";
            Ui.Line($"  {i + 1}. {s.ProcessName}{title} (processo {s.ProcessId}) {(s.Active ? "tocando" : "parado")} {bar}");
        }
        return sessions;
    }

    /// <summary>Acha o jogo pelo argumento (número do processo ou nome) ou pergunta na lista.</summary>
    public static AudioSessionInfo? Pick(string? hint)
    {
        var sessions = ShowGames();
        if (sessions.Count == 0) return null;
        if (hint is not null)
        {
            var match = uint.TryParse(hint, out var pid)
                ? sessions.FirstOrDefault(s => s.ProcessId == pid)
                : sessions.FirstOrDefault(s => s.ProcessName.Contains(hint, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
            Ui.Warn($"não achei \"{hint}\" na lista.");
        }
        var answer = Ui.Ask("Número do jogo na lista:", "1");
        return int.TryParse(answer, out var n) && n >= 1 && n <= sessions.Count ? sessions[n - 1] : null;
    }

    public static async Task RecordAsync(AudioSessionInfo game, AppPaths paths, TimeSpan limit)
    {
        Ui.Line();
        Ui.Line("Vai gravar SÓ o som deste programa, sem microfone, sem Discord e sem outros programas.");
        Ui.Line($"O arquivo fica no seu PC, em {paths.Recordings}, e nada é enviado para lugar nenhum.");
        if (!Ui.YesNo("Gravar?", true)) return;

        Directory.CreateDirectory(paths.Recordings);
        var file = Path.Combine(paths.Recordings, $"{DateTime.Now:yyyyMMdd-HHmmss}-{game.ProcessName}.wav");
        await using var capture = await ProcessAudioCapture.StartAsync(game.ProcessId, file);
        Ui.Ok($"Gravando em {Path.GetFileName(file)}. Jogue normalmente; aperte Enter para parar (limite {Ui.Clock(limit)}).");

        var stop = WaitForEnter();
        var watch = Stopwatch.StartNew();
        float peak = 0;
        while (!stop.IsCompleted && watch.Elapsed < limit)
        {
            while (capture.Chunks.TryRead(out var chunk)) peak = Math.Max(peak, chunk.Samples.Max(Math.Abs));
            Console.Write($"\r  {Ui.Clock(watch.Elapsed)}  nível {new string('|', (int)(peak * 30)),-30}  ");
            peak *= 0.6f;
            await Task.WhenAny(stop, Task.Delay(250));
        }
        Console.WriteLine();
        Ui.Ok($"Gravado: {Ui.Clock(capture.RecordingDuration ?? watch.Elapsed)} em {file}");
        if (capture.Glitches > 0) Ui.Warn($"o Windows perdeu {capture.Glitches} pedaço(s) de áudio na gravação.");
        if (capture.Captured < TimeSpan.FromSeconds(1)) Ui.Warn("quase nada foi capturado. O jogo estava tocando som?");
    }

    /// <summary>Termina quando o usuário aperta Enter (ou fecha a entrada).</summary>
    public static Task WaitForEnter() => Task.Run(() =>
    {
        while (true)
        {
            if (Console.IsInputRedirected)
            {
                Console.ReadLine();
                return;
            }
            var key = Console.ReadKey(intercept: true);
            if (key.Key is ConsoleKey.Enter or ConsoleKey.Escape) return;
        }
    });

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
