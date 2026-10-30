using MpGameDub.Core.Text;

namespace MpGameDub.Tests.Text;

public class TranscriptFilterTests
{
    [Theory]
    [InlineData(" Get down!  ", "Get down!")]
    [InlineData("[laughs] We made it.", "We made it.")]
    [InlineData("(sighs) Fine. *coughs* Let's go.", "Fine. Let's go.")]
    [InlineData("♪ la la ♪ Where is she?", "Where is she?")]
    [InlineData("Oh, okay.", "Oh, okay.")] // interjeição com palavra de verdade fica
    [InlineData("Uh, what? I meant yes, sir.", "Uh, what? I meant yes, sir.")]
    [InlineData("Huh? Who are you?", "Huh? Who are you?")]
    [InlineData("Hi!", "Hi!")]
    [InlineData("Ah, Jimmy.", "Ah, Jimmy.")]
    public void RealSpeech_IsCleanedAndAccepted(string raw, string expected)
    {
        var r = TranscriptFilter.Apply(raw);
        Assert.True(r.Accepted, r.Reason);
        Assert.Equal(expected, r.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[Music]")]
    [InlineData("♪♪♪")]
    [InlineData("...")]
    [InlineData(" Thanks for watching!")]
    [InlineData("Subtitles by the Amara.org community")]
    [InlineData("you you you you you")]
    [InlineData("Hmm.")]
    [InlineData(" Mmm.")]
    [InlineData("- Mm.")]
    [InlineData("Um...")]
    [InlineData("Eesh!")]
    [InlineData("Uh-huh.")]
    [InlineData("Mm-hmm, uh...")]
    [InlineData("Oh!")]
    public void Hallucinations_AreRejected(string raw)
    {
        var r = TranscriptFilter.Apply(raw);
        Assert.False(r.Accepted);
        Assert.NotNull(r.Reason);
    }
}
