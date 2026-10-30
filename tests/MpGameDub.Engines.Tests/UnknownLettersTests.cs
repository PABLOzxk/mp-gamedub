namespace MpGameDub.Engines.Tests;

public class UnknownLettersTests
{
    // Peças que o vocabulário de verdade tem: as palavras minúsculas existem, as maiúsculas com acento não.
    private static readonly Dictionary<string, int> Vocab = new[] { "▁à", "▁às", "▁ó", "▁ótimo", "▁ótima", "▁último", "▁ônibus", "▁noite", "timo" }
        .Select((piece, id) => (piece, id)).ToDictionary(p => p.piece, p => p.id);

    [Theory]
    [InlineData("É um lixo. <unk>timo.", "É um lixo. Ótimo.")]
    [InlineData("<unk>timo trabalho, Jimmy.", "Ótimo trabalho, Jimmy.")]
    [InlineData("<unk>s vezes eu choro.", "Às vezes eu choro.")]
    [InlineData("<unk> noite, saímos.", "À noite, saímos.")]
    [InlineData("<unk>ltimo aviso.", "Último aviso.")]
    [InlineData("<unk>nibus!", "Ônibus!")]
    [InlineData("<unk>timas notícias.", "Ótimas notícias.")] // "▁ótimas" não existe, "▁ótima" começa a palavra
    public void Letra_maiuscula_que_o_modelo_nao_sabe_escrever_volta(string decoded, string expected) =>
        Assert.Equal(expected, UnknownLetters.Repair(decoded, Vocab));

    [Theory]
    [InlineData("Custa 5 <unk>.", "Custa 5.")]
    [InlineData("<unk>", "")]
    [InlineData("<unk>xyz vezes.", "xyz vezes.")]
    public void Sem_palpite_o_unk_some_do_texto(string decoded, string expected) =>
        Assert.Equal(expected, UnknownLetters.Repair(decoded, Vocab));

    [Fact]
    public void Texto_sem_unk_fica_igual() =>
        Assert.Equal("Vamos, Jimmy.", UnknownLetters.Repair("Vamos, Jimmy.", Vocab));
}
