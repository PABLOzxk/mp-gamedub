using MpGameDub.Core.Text;

namespace MpGameDub.Tests.Text;

public class SentenceSplitterTests
{
    [Theory]
    [InlineData("Reload! I'm out of ammo!", new[] { "Reload!", "I'm out of ammo!" })]
    [InlineData("The reactor will overload in five minutes. Move!", new[] { "The reactor will overload in five minutes.", "Move!" })]
    [InlineData("Wait... did you hear that?", new[] { "Wait...", "did you hear that?" })]
    [InlineData("What?! No way.", new[] { "What?!", "No way." })]
    [InlineData("He said \"Run.\" Then he left.", new[] { "He said \"Run.\"", "Then he left." })]
    public void Divide_nas_pontuacoes_finais(string text, string[] expected)
    {
        Assert.Equal(expected, SentenceSplitter.Split(text));
    }

    [Theory]
    [InlineData("Mr. Smith is waiting for us.")]
    [InlineData("Tell Sgt. Davis to hold the line.")]
    [InlineData("I owe you 3.5 million credits.")]
    [InlineData("I told you, we don't have time for this.")]
    [InlineData("No punctuation at the end")]
    public void Nao_divide_abreviacoes_numeros_nem_frase_unica(string text)
    {
        Assert.Equal([text], SentenceSplitter.Split(text));
    }

    [Fact]
    public void Abreviacao_seguida_de_outra_frase_divide_so_no_fim()
    {
        Assert.Equal(["Mr. Smith is waiting for us.", "Hurry!"], SentenceSplitter.Split("Mr. Smith is waiting for us. Hurry!"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Texto_vazio_nao_tem_frases(string text)
    {
        Assert.Empty(SentenceSplitter.Split(text));
    }
}
