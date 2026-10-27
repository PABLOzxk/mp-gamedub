using MpGameDub.Core.Text;

namespace MpGameDub.Tests.Text;

public class TranscriptFilterTests
{
    [Theory]
    [InlineData(" Get down!  ", "Get down!")]
    [InlineData("[laughs] We made it.", "We made it.")]
    [InlineData("(sighs) Fine. *coughs* Let's go.", "Fine. Let's go.")]
    [InlineData("♪ la la ♪ Where is she?", "Where is she?")]
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
    public void Hallucinations_AreRejected(string raw)
    {
        var r = TranscriptFilter.Apply(raw);
        Assert.False(r.Accepted);
        Assert.NotNull(r.Reason);
    }
}
