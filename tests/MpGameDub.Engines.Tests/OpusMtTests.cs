using System.Text.Json;
using System.Text.Json.Serialization;

namespace MpGameDub.Engines.Tests;

/// <summary>
/// Confere o tradutor contra a referência que ferramentas/exportar-opus-mt.py grava junto com o modelo:
/// mesmos ids do tokenizador do transformers e mesma tradução gulosa com os mesmos arquivos ONNX.
/// Roda quando MPGAMEDUB_OPUSMT aponta para a pasta exportada (o CI de modelos faz isso).
/// </summary>
public class OpusMtTests
{
    private static readonly string? Folder = Environment.GetEnvironmentVariable("MPGAMEDUB_OPUSMT");

    [OpusMtFact]
    public void Tokenizador_gera_os_mesmos_ids_do_transformers()
    {
        using var translator = new OpusMtTranslator(Folder!, ComputeDevice.Cpu, cpuThreads: 4);
        foreach (var item in Reference())
            Assert.Equal(item.Ids, translator.Encode(item.Text));
    }

    [OpusMtFact]
    public async Task Traducao_gulosa_bate_com_a_referencia()
    {
        using var translator = new OpusMtTranslator(Folder!, ComputeDevice.Cpu, cpuThreads: 4);
        foreach (var item in Reference())
            Assert.Equal(item.Translation, await translator.TranslateAsync(item.Text, CancellationToken.None));
    }

    [OpusMtFact]
    public async Task Texto_vazio_nao_chama_o_modelo()
    {
        using var translator = new OpusMtTranslator(Folder!, ComputeDevice.Cpu, cpuThreads: 1);
        Assert.Equal("", await translator.TranslateAsync("   ", CancellationToken.None));
    }

    private static Entry[] Reference() =>
        JsonSerializer.Deserialize<Entry[]>(File.ReadAllText(Path.Combine(Folder!, "referencia.json")))!;

    private sealed record Entry(
        [property: JsonPropertyName("texto")] string Text,
        [property: JsonPropertyName("ids")] int[] Ids,
        [property: JsonPropertyName("traducao")] string Translation);
}

public sealed class OpusMtFactAttribute : FactAttribute
{
    public OpusMtFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MPGAMEDUB_OPUSMT")))
            Skip = "Defina MPGAMEDUB_OPUSMT com a pasta do Opus-MT exportado.";
    }
}
