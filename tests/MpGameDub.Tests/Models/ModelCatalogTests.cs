using MpGameDub.Core.Models;

namespace MpGameDub.Tests.Models;

public class ModelCatalogTests
{
    [Fact]
    public void Todo_modelo_tem_origem_https_hash_tamanho_e_licenca()
    {
        foreach (var model in ModelCatalog.All)
        {
            Assert.Equal("https", model.Url.Scheme);
            Assert.Matches("^[0-9a-f]{64}$", model.Sha256);
            Assert.True(model.Size > 0, model.Id);
            Assert.False(string.IsNullOrWhiteSpace(model.License), model.Id);
        }
    }

    [Fact]
    public void Ids_e_nomes_de_arquivo_nao_se_repetem()
    {
        Assert.Equal(ModelCatalog.All.Count, ModelCatalog.All.Select(m => m.Id).Distinct().Count());
        Assert.Equal(ModelCatalog.All.Count, ModelCatalog.All.Select(m => m.FileName).Distinct().Count());
    }

    [Fact]
    public void Traducao_vem_em_zip_da_release_deste_repositorio()
    {
        Assert.True(ModelCatalog.OpusMt.Extract);
        Assert.Equal("opus-mt-tc-big-en-pt-int8.zip", ModelCatalog.OpusMt.FileName);
        Assert.Equal("github.com", ModelCatalog.OpusMt.Url.Host);
        Assert.StartsWith("/PABLOzxk/mp-gamedub/releases/download/", ModelCatalog.OpusMt.Url.AbsolutePath);
        Assert.Contains(ModelCatalog.OpusMt, ModelCatalog.All);
    }

    [Fact]
    public void Busca_por_id_ignora_maiusculas()
    {
        Assert.Same(ModelCatalog.WhisperTurbo, ModelCatalog.Find("WHISPER-LARGE-V3-TURBO-Q5_0"));
        Assert.Null(ModelCatalog.Find("nao-existe"));
    }
}
