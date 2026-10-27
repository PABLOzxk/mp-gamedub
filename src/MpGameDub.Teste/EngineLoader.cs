using System.Collections.Concurrent;
using System.Diagnostics;
using MpGameDub.Core.Models;
using MpGameDub.Core.Pipeline;
using MpGameDub.Core.Speech;
using MpGameDub.Engines;
using MpGameDub.Windows;
using Whisper.net.LibraryLoader;
using Whisper.net.Logger;

namespace MpGameDub.Teste;

/// <summary>Os motores carregados de uma rodada, com o que deu errado no caminho anotado para o relatório.</summary>
internal sealed class LoadedEngines(IVoiceActivityDetector vad, UtteranceProcessor processor, List<string> notes, IDisposable[] owned) : IDisposable
{
    public IVoiceActivityDetector Vad { get; } = vad;
    public UtteranceProcessor Processor { get; } = processor;
    public List<string> Notes { get; } = notes;
    public double LoadMs { get; init; }

    public void Dispose()
    {
        foreach (var item in owned.Reverse()) item.Dispose();
    }
}

internal static class EngineLoader
{
    private static readonly ConcurrentQueue<string> NativeLog = new();
    private static bool _runtimeChosen;

    /// <summary>
    /// O whisper.cpp escolhe a biblioteca nativa (Vulkan ou CPU) uma vez por processo. A escolha vale para o
    /// resto da execução; trocar exige abrir o programa de novo.
    /// </summary>
    public static void ChooseWhisperRuntime(bool cpuOnly)
    {
        if (_runtimeChosen) return;
        RuntimeOptions.RuntimeLibraryOrder = cpuOnly ? [RuntimeLibrary.Cpu] : [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu];
        LogProvider.AddLogger((level, message) =>
        {
            if (level <= WhisperLogLevel.Warning || message?.Contains("vulkan", StringComparison.OrdinalIgnoreCase) == true)
                NativeLog.Enqueue($"[{level}] {message?.TrimEnd()}");
        });
        _runtimeChosen = true;
    }

    public static IReadOnlyList<string> NativeLogLines => NativeLog.ToArray();

    /// <summary>Os modelos que esta rodada precisa, para baixar antes de começar.</summary>
    public static List<ModelFile> Required(RunOptions options)
    {
        var models = new List<ModelFile> { ModelCatalog.SileroVad, options.Whisper };
        if (options.Speak) models.Add(ModelCatalog.Kokoro);
        if (options.Translate && options.OpusMtFolder is null) models.Add(ModelCatalog.OpusMt);
        return models;
    }

    public static async Task<bool> EnsureModelsAsync(IEnumerable<ModelFile> models, AppPaths paths, bool ask)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MpGameDubTeste/0.1");
        var store = new ModelStore(paths.Models, http);
        var missing = models.Where(m => !store.IsReady(m)).ToList();
        if (missing.Count == 0) return true;

        Ui.Line("Faltam estes modelos (baixados uma vez, conferidos pelo SHA-256 e guardados em modelos\\):");
        foreach (var m in missing) Ui.Line($"  - {m.Description}, licença {m.License}, de {m.Url.Host}");
        Ui.Line($"  Total: {Ui.Megabytes(missing.Sum(m => m.Size))}");
        if (ask && !Ui.YesNo("Baixar agora?", true)) return false;

        foreach (var model in missing)
        {
            try
            {
                await store.EnsureAsync(model, new Ui.ProgressBar(model.Id), CancellationToken.None);
            }
            catch (Exception ex) when (ex is HttpRequestException or ModelIntegrityException or IOException or TaskCanceledException)
            {
                Ui.Line();
                Ui.Error($"não deu para baixar {model.Id}: {ex.Message}");
                return false;
            }
        }
        Ui.Ok("Modelos conferidos.");
        return true;
    }

    public static async Task<LoadedEngines> LoadAsync(RunOptions options, AppPaths paths)
    {
        var watch = Stopwatch.StartNew();
        var store = new ModelStore(paths.Models, new HttpClient());
        var notes = new List<string>();
        var owned = new List<IDisposable>();
        ChooseWhisperRuntime(options.CpuOnly);

        var gpu = options.CpuOnly ? null : GpuAdapters.Best();
        if (gpu is not null) DirectMlAdapter.Index = gpu.Index;

        try
        {
            Ui.Info("Carregando o detector de fala...");
            var vad = new SileroVad(store.PathOf(ModelCatalog.SileroVad));
            owned.Add(vad);

            Ui.Info($"Carregando {options.Whisper.Description}...");
            var recognizer = new WhisperRecognizer(store.PathOf(options.Whisper), "en", useGpu: !options.CpuOnly, options.CpuThreads);
            owned.Add(recognizer);
            var loaded = RuntimeOptions.LoadedLibrary?.ToString() ?? "desconhecida";
            notes.Add($"Biblioteca do Whisper carregada: {loaded}.");
            if (!options.CpuOnly && RuntimeOptions.LoadedLibrary != RuntimeLibrary.Vulkan)
                notes.Add("O Whisper não conseguiu usar a placa (Vulkan) e caiu para a CPU. Veja motores.log.");

            ITranslator? translator = null;
            if (options.Translate)
            {
                var folder = options.OpusMtFolder ?? store.PathOf(ModelCatalog.OpusMt);
                if (File.Exists(Path.Combine(folder, "encoder_model.onnx")))
                {
                    Ui.Info("Carregando a tradução Opus-MT...");
                    // A tradução fica na CPU: são frases curtas, e a placa fica para o Whisper, a voz e o jogo.
                    translator = new OpusMtTranslator(folder, ComputeDevice.Cpu, options.CpuThreads);
                    owned.Add(translator);
                }
                else
                {
                    notes.Add($"Tradução desligada: o modelo Opus-MT não está em {folder}. A legenda sai em inglês e não há voz.");
                    Ui.Warn("o modelo de tradução não foi encontrado; esta rodada mede só o reconhecimento.");
                }
            }

            ISpeechSynthesizer? synthesizer = null;
            if (options.Speak && translator is not null)
            {
                Ui.Info($"Carregando a voz {options.Voice}...");
                synthesizer = await CreateVoiceAsync(store.PathOf(ModelCatalog.Kokoro), options, gpu, notes);
                owned.Add(synthesizer);
            }

            var processor = new UtteranceProcessor(new EngineSet(recognizer, translator, synthesizer), options.StageTimeout);
            return new LoadedEngines(vad, processor, notes, owned.ToArray()) { LoadMs = watch.Elapsed.TotalMilliseconds };
        }
        catch
        {
            foreach (var item in Enumerable.Reverse(owned)) item.Dispose();
            throw;
        }
    }

    private static async Task<ISpeechSynthesizer> CreateVoiceAsync(string modelPath, RunOptions options, GpuAdapter? gpu, List<string> notes)
    {
        KokoroSynthesizer OnCpu() => new(modelPath, options.Voice, ComputeDevice.Cpu, options.CpuThreads);
        if (gpu is null) return OnCpu();

        var opened = await VoiceFallback.OpenAsync(
            () => new KokoroSynthesizer(modelPath, options.Voice, ComputeDevice.Gpu, options.CpuThreads), OnCpu, CancellationToken.None);
        if (opened.OnGpu)
        {
            notes.Add($"Placa usada pela voz: {gpu.Name} ({Ui.Megabytes(gpu.DedicatedMemory)}), índice DXGI {gpu.Index}.");
        }
        else
        {
            notes.Add($"A voz falhou na placa {gpu.Name} (DirectML: {opened.Failure}) e foi para a CPU.");
            Ui.Warn("a voz não funcionou na placa de vídeo; usando o processador.");
        }
        return opened.Synthesizer;
    }
}
