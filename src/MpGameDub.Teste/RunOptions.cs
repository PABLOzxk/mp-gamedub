using System.Globalization;
using MpGameDub.Core.Models;

namespace MpGameDub.Teste;

/// <summary>Pastas do programa. Tudo fica ao lado do .exe, para apagar a pasta e não sobrar nada.</summary>
internal sealed class AppPaths
{
    public AppPaths(string root)
    {
        Root = root;
        Models = Path.Combine(root, "modelos");
        Recordings = Path.Combine(root, "gravacoes");
        Reports = Path.Combine(root, "relatorios");
        // Fora da pasta do programa de propósito: se ela for apagada com o jogo baixo, a próxima versão ainda devolve o volume.
        VolumeState = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MpGameDub", "volume-original.json");
    }

    public string Root { get; }
    public string Models { get; }
    public string Recordings { get; }
    public string Reports { get; }
    public string VolumeState { get; }
    public string Example => Path.Combine(Root, "exemplo.wav");

    public string NewReportFolder(string name)
    {
        var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '-' : c));
        var folder = Path.Combine(Reports, $"{DateTime.Now:yyyyMMdd-HHmmss}-{safe}");
        Directory.CreateDirectory(folder);
        return folder;
    }
}

/// <summary>Escolhas de uma rodada, vindas da linha de comando ou do menu.</summary>
internal sealed record RunOptions
{
    public static readonly IReadOnlyDictionary<string, ModelFile> WhisperChoices = new Dictionary<string, ModelFile>
    {
        ["turbo"] = ModelCatalog.WhisperTurbo,
        ["turbo8"] = ModelCatalog.WhisperTurbo8,
        ["medium"] = ModelCatalog.WhisperMedium,
        ["small"] = ModelCatalog.WhisperSmall,
    };

    public static readonly string[] Voices = ["pf_dora", "pm_alex", "pm_santa"];

    public ModelFile Whisper { get; init; } = ModelCatalog.WhisperTurbo;
    public bool CpuOnly { get; init; }
    public string Voice { get; init; } = "pf_dora";
    public bool Speak { get; init; } = true;
    public bool Translate { get; init; } = true;
    public string? OpusMtFolder { get; init; }
    public float GameGainWhileSpeaking { get; init; } = 0.3f;
    public TimeSpan MaxAge { get; init; } = TimeSpan.FromSeconds(5);
    public int CpuThreads { get; init; } = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);
    public bool Record { get; init; }

    public string ShortName => $"{WhisperChoices.First(p => p.Value == Whisper).Key}{(CpuOnly ? "-cpu" : "")}{(Speak ? "-" + Voice : "-legenda")}";

    /// <summary>Lê as opções conhecidas e devolve o resto (comando e argumentos posicionais).</summary>
    public static (RunOptions Options, List<string> Positional) Parse(IReadOnlyList<string> args)
    {
        var options = new RunOptions();
        var rest = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            string Next() => i + 1 < args.Count ? args[++i] : throw new ArgumentException($"Falta o valor depois de {arg}.");
            options = arg.ToLowerInvariant() switch
            {
                "--whisper" => options with { Whisper = WhisperChoices.TryGetValue(Next().ToLowerInvariant(), out var m) ? m : throw new ArgumentException($"Whisper desconhecido. Use: {string.Join(", ", WhisperChoices.Keys)}.") },
                "--cpu" => options with { CpuOnly = true },
                "--voz" => options with { Voice = Voices.Contains(Next()) ? args[i] : throw new ArgumentException($"Voz desconhecida. Use: {string.Join(", ", Voices)}.") },
                "--sem-voz" => options with { Speak = false },
                "--sem-traducao" => options with { Translate = false, Speak = false },
                "--opusmt" => options with { OpusMtFolder = Next() },
                "--volume-jogo" => options with { GameGainWhileSpeaking = Percent(Next()) },
                "--max-atraso" => options with { MaxAge = TimeSpan.FromSeconds(Number(Next())) },
                "--threads" => options with { CpuThreads = (int)Number(Next()) },
                "--gravar" => options with { Record = true },
                _ when arg.StartsWith("--", StringComparison.Ordinal) => throw new ArgumentException($"Opção desconhecida: {arg}."),
                _ => Add(rest, arg, options),
            };
        }
        return (options, rest);
    }

    private static RunOptions Add(List<string> rest, string arg, RunOptions options)
    {
        rest.Add(arg);
        return options;
    }

    private static double Number(string text) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : throw new ArgumentException($"Número inválido: {text}.");

    private static float Percent(string text) => (float)Math.Clamp(Number(text) / 100, 0, 1);
}
