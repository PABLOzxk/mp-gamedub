using MpGameDub.Core.Audio;
using MpGameDub.Core.Models;

namespace MpGameDub.Engines.Tests;

/// <summary>
/// Os testes de motor usam os modelos de verdade, baixados uma vez e conferidos pelo hash.
/// A pasta vem de MPGAMEDUB_MODELOS (o CI guarda em cache) ou cai na pasta temporária.
/// </summary>
internal static class TestModels
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static string Folder { get; } =
        Environment.GetEnvironmentVariable("MPGAMEDUB_MODELOS") is { Length: > 0 } folder
            ? folder
            : Path.Combine(Path.GetTempPath(), "mp-gamedub-modelos");

    public static async Task<string> GetAsync(ModelFile model)
    {
        await Gate.WaitAsync();
        try
        {
            return await new ModelStore(Folder, Http).EnsureAsync(model, null, CancellationToken.None);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static AudioClip Jfk() => WavFile.Read(Fixture("jfk.wav"));

    public static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}

/// <summary>Teste que baixa centenas de MB: só roda com MPGAMEDUB_TESTES_PESADOS=1.</summary>
public sealed class HeavyFactAttribute : FactAttribute
{
    public HeavyFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("MPGAMEDUB_TESTES_PESADOS") != "1")
            Skip = "Teste pesado: defina MPGAMEDUB_TESTES_PESADOS=1 para rodar.";
    }
}
