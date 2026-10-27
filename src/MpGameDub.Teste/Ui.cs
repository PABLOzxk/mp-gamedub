using System.Globalization;

namespace MpGameDub.Teste;

/// <summary>Conversa com o usuário no console, em português, sem depender de cores para entender.</summary>
internal static class Ui
{
    private static readonly Lock Gate = new();
    public static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    public static void Title(string text)
    {
        Line();
        Line(text, ConsoleColor.Cyan);
        Line(new string('=', Math.Min(text.Length, 70)), ConsoleColor.Cyan);
    }

    public static void Line(string text = "", ConsoleColor? color = null)
    {
        lock (Gate)
        {
            if (color is { } c) Console.ForegroundColor = c;
            Console.WriteLine(text);
            if (color is not null) Console.ResetColor();
        }
    }

    public static void Info(string text) => Line(text, ConsoleColor.Gray);
    public static void Ok(string text) => Line(text, ConsoleColor.Green);
    public static void Warn(string text) => Line("Atenção: " + text, ConsoleColor.Yellow);
    public static void Error(string text) => Line("Erro: " + text, ConsoleColor.Red);

    public static string Ask(string question, string? fallback = null)
    {
        lock (Gate)
        {
            Console.Write(fallback is null ? $"{question} " : $"{question} [{fallback}] ");
        }
        // Com a resposta vinda de um pipe ("'s' | MpGameDubTeste.exe"), a primeira linha pode trazer a marca de
        // ordem de bytes do UTF-8, e "\uFEFFs" não é "s".
        var answer = Console.ReadLine()?.Replace("\uFEFF", "").Trim() ?? "";
        return answer.Length == 0 && fallback is not null ? fallback : answer;
    }

    public static bool YesNo(string question, bool fallback)
    {
        while (true)
        {
            var answer = Ask($"{question} (s/n)", fallback ? "s" : "n").ToLowerInvariant();
            if (answer is "s" or "sim" or "y") return true;
            if (answer is "n" or "nao" or "não") return false;
        }
    }

    public static int Choose(string question, IReadOnlyList<string> options, int fallback = 1)
    {
        for (var i = 0; i < options.Count; i++) Line($"  {i + 1}. {options[i]}");
        while (true)
        {
            var answer = Ask(question, fallback.ToString(Br));
            if (int.TryParse(answer, out var n) && n >= 1 && n <= options.Count) return n - 1;
            Warn($"responda com um número de 1 a {options.Count}.");
        }
    }

    public static double? AskNumber(string question)
    {
        var answer = Ask(question + " (Enter para pular)", "");
        return double.TryParse(answer.Replace('.', ','), NumberStyles.Float, Br, out var value) ? value : null;
    }

    public static void Pause()
    {
        Line();
        Info("Aperte Enter para continuar.");
        Console.ReadLine();
    }

    /// <summary>Barra de progresso numa linha só, reescrita no lugar.</summary>
    public sealed class ProgressBar(string label) : IProgress<double>
    {
        private int _last = -1;

        public void Report(double value)
        {
            var percent = (int)Math.Clamp(value * 100, 0, 100);
            if (percent == _last) return;
            _last = percent;
            var filled = percent / 4;
            lock (Gate) Console.Write($"\r  {label} [{new string('#', filled)}{new string('.', 25 - filled)}] {percent,3}%");
            if (percent == 100) lock (Gate) Console.WriteLine();
        }
    }

    public static string Seconds(double ms) => (ms / 1000).ToString("0.0", Br) + " s";
    public static string Clock(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss", Br) : t.ToString(@"mm\:ss", Br);
    public static string Megabytes(long bytes) => bytes >= 1L << 30 ? (bytes / 1073741824.0).ToString("0.0", Br) + " GB" : (bytes / 1048576.0).ToString("0", Br) + " MB";
}
