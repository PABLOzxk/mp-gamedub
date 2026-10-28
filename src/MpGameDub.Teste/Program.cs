using System.Text;
using MpGameDub.Core.Models;
using MpGameDub.Teste;
using MpGameDub.Windows;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;
var paths = new AppPaths(AppContext.BaseDirectory);

try
{
    if (GameVolume.RestorePending(paths.VolumeState) is { } restored) Ui.Warn($"a última sessão fechou com o jogo abaixado. {restored}");
    if (!SystemInfo.SupportsProcessLoopback)
    {
        Ui.Error($"este Windows (build {Environment.OSVersion.Version.Build}) não tem a captura de som por programa. É preciso Windows 11 ou Windows 10 atualizado (build 20348 ou mais nova).");
        Ui.Pause();
        return 1;
    }

    var (options, rest) = RunOptions.Parse(args);
    if (rest.Count > 0) return await Commands.RunAsync(rest, options, paths) ? 0 : 1;
    await Commands.MenuAsync(options, paths);
    return 0;
}
catch (Exception ex) when (ex is not OutOfMemoryException)
{
    var log = ErrorLog.Write(paths, ex);
    Ui.Error(ex.Message);
    Ui.Line($"Os detalhes ficaram em {log}. Mande esse arquivo junto com o que você estava fazendo.");
    Ui.Pause();
    return 1;
}

namespace MpGameDub.Teste
{
    /// <summary>O erro.txt para mandar quando algo dá errado: a exceção inteira e o log dos motores.</summary>
    internal static class ErrorLog
    {
        public static string Write(AppPaths paths, Exception ex)
        {
            var log = Path.Combine(paths.Root, "erro.txt");
            File.WriteAllText(log, $"{DateTime.Now:O}\n{ex}\n\nMotores:\n{string.Join('\n', EngineLoader.NativeLogLines)}");
            return log;
        }
    }

    internal static class Commands
    {
        private const string Usage = """
            Uso: MpGameDubTeste [comando] [opções]
              (sem comando)          abre o menu
              jogos                  lista os programas tocando som
              modelos                baixa e confere todos os modelos
              gravar [jogo]          grava só o som do jogo (você escolhe quando)
              lote <arquivo.wav>     processa uma gravação como se fosse ao vivo
              comparar <arquivo.wav> roda a gravação com cada Whisper e compara
              exemplo                processa o áudio de exemplo que vem junto
              aovivo [jogo]          legenda e voz enquanto você joga
            Opções:
              --whisper turbo|turbo8|medium|small   (padrão turbo)
              --voz pf_dora|pm_alex|pm_santa         (padrão pf_dora)
              --sem-voz        só legenda em português
              --sem-traducao   só reconhecimento, legenda em inglês
              --cpu            tudo no processador, sem usar a placa
              --volume-jogo N  volume do jogo durante a voz, em % (padrão 30)
              --max-atraso N   segundos até uma fala atrasada ser descartada (padrão 5)
              --gravar         no modo ao vivo, grava o som do jogo para montar o dublado.wav
              --opusmt PASTA   pasta com o Opus-MT exportado
            """;

        public static async Task<bool> RunAsync(List<string> rest, RunOptions options, AppPaths paths)
        {
            string? Arg(int i) => rest.Count > i ? rest[i] : null;
            switch (rest[0].ToLowerInvariant())
            {
                case "jogos":
                    GameCommands.ShowGames();
                    return true;
                case "modelos":
                    return await EngineLoader.EnsureModelsAsync(ModelCatalog.All, paths, ask: false);
                case "gravar":
                    if (GameCommands.Pick(Arg(1)) is not { } recordGame) return false;
                    await GameCommands.RecordAsync(recordGame, paths, TimeSpan.FromMinutes(30));
                    return true;
                case "lote" when Arg(1) is { } file:
                    return await BatchCommand.RunAsync(file, options, paths) is not null;
                case "comparar" when Arg(1) is { } file:
                    await BatchCommand.CompareAsync(file, options, paths);
                    return true;
                case "exemplo":
                    return await BatchCommand.RunAsync(paths.Example, options, paths, "exemplo que vem com o programa") is not null;
                case "aovivo":
                    if (GameCommands.Pick(Arg(1)) is not { } liveGame) return false;
                    await LiveSession.RunAsync(liveGame, options, paths);
                    return true;
                default:
                    Ui.Line(Usage);
                    return rest[0] is "ajuda" or "--ajuda" or "-h" or "--help";
            }
        }

        public static async Task MenuAsync(RunOptions options, AppPaths paths)
        {
            Ui.Title("MP GameDub, teste da Fase 0");
            Ui.Info("Este programa mede se dá para traduzir e dublar as falas de um jogo no seu PC.");
            Ui.Info("Ele só ouve o som do jogo pelo Windows: não mexe no jogo, não pede administrador e não manda nada para a internet.");
            string[] items =
            [
                "Testar com o áudio de exemplo (comece por aqui)",
                "Gravar o som de um jogo",
                "Processar uma gravação (relatório + dublado.wav)",
                "Comparar os modelos do Whisper numa gravação",
                "Ao vivo: legenda e voz enquanto joga",
                "Baixar e conferir todos os modelos",
                "Trocar opções (voz, placa ou CPU, só legenda)",
                "Sair",
            ];
            while (true)
            {
                Ui.Line();
                Ui.Info($"Opções atuais: Whisper {options.Whisper.Id}, {(options.CpuOnly ? "só CPU" : "placa de vídeo")}, {(options.Speak ? "voz " + options.Voice : options.Translate ? "só legenda" : "sem tradução")}.");
                var choice = Ui.Choose("Escolha:", items);
                try
                {
                    switch (choice)
                    {
                        case 0:
                            await BatchCommand.RunAsync(paths.Example, options, paths, "exemplo que vem com o programa");
                            break;
                        case 1:
                            if (GameCommands.Pick(null) is { } recordGame) await GameCommands.RecordAsync(recordGame, paths, TimeSpan.FromMinutes(30));
                            break;
                        case 2:
                            if (PickRecording(paths) is { } file) await BatchCommand.RunAsync(file, options, paths);
                            break;
                        case 3:
                            if (PickRecording(paths) is { } compareFile) await BatchCommand.CompareAsync(compareFile, options, paths);
                            break;
                        case 4:
                            if (GameCommands.Pick(null) is { } liveGame)
                                await LiveSession.RunAsync(liveGame, options with { Record = Ui.YesNo("Gravar o som do jogo para montar o dublado.wav no fim?", false) }, paths);
                            break;
                        case 5:
                            await EngineLoader.EnsureModelsAsync(ModelCatalog.All, paths, ask: true);
                            break;
                        case 6:
                            options = ChangeOptions(options);
                            break;
                        default:
                            return;
                    }
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
                {
                    Ui.Error(ex.Message);
                }
                Ui.Pause();
            }
        }

        private static string? PickRecording(AppPaths paths)
        {
            var files = Directory.Exists(paths.Recordings)
                ? Directory.GetFiles(paths.Recordings, "*.wav").OrderByDescending(File.GetLastWriteTime).Take(9).ToList()
                : [];
            if (files.Count == 0)
            {
                var typed = Ui.Ask("Nenhuma gravação em gravacoes\\. Caminho de um .wav:", "");
                return typed.Length > 0 ? typed.Trim('"') : null;
            }
            var labels = files.Select(f => $"{Path.GetFileName(f)} ({Ui.Megabytes(new FileInfo(f).Length)})").Append("Outro arquivo...").ToList();
            var choice = Ui.Choose("Qual gravação?", labels);
            if (choice < files.Count) return files[choice];
            var other = Ui.Ask("Caminho do .wav:", "");
            return other.Length > 0 ? other.Trim('"') : null;
        }

        private static RunOptions ChangeOptions(RunOptions options)
        {
            var whisper = RunOptions.WhisperChoices.ToList();
            var w = Ui.Choose("Modelo do Whisper:", whisper.Select(p => p.Value.Description).ToList(), whisper.FindIndex(p => p.Value == options.Whisper) + 1);
            var output = Ui.Choose("Saída:", ["Legenda e voz em português", "Só legenda em português", "Só reconhecimento (legenda em inglês)"], options.Speak ? 1 : options.Translate ? 2 : 3);
            var voice = options.Voice;
            if (output == 0)
                voice = RunOptions.Voices[Ui.Choose("Voz:", ["pf_dora (feminina)", "pm_alex (masculina)", "pm_santa (masculina)"], Array.IndexOf(RunOptions.Voices, options.Voice) + 1)];
            var cpu = Ui.Choose("Onde rodar:", ["Placa de vídeo (recomendado)", "Só processador (para comparar)"], options.CpuOnly ? 2 : 1) == 1;
            if (cpu != options.CpuOnly)
                Ui.Warn("trocar entre placa e CPU no Whisper só vale depois de fechar e abrir o programa de novo.");
            return options with
            {
                Whisper = whisper[w].Value,
                Speak = output == 0,
                Translate = output < 2,
                Voice = voice,
                CpuOnly = cpu,
            };
        }
    }
}
