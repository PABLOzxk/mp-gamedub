using MpGameDub.Core.Speech;

namespace MpGameDub.Tests.Speech;

public class SpeechSegmenterTests
{
    private const int Frame = 512; // 32 ms a 16 kHz

    // Cada caractere é um quadro de 32 ms: 'f' = fala (0,9), '.' = silêncio (0,05), 'm' = meio-termo (0,4).
    private static List<SpeechSegment> Run(string pattern, SegmenterOptions? options = null)
    {
        var segmenter = new SpeechSegmenter(Frame, options);
        var result = new List<SpeechSegment>();
        var index = 0;
        foreach (var c in pattern)
        {
            var frame = Enumerable.Repeat((float)index++, Frame).ToArray(); // o valor marca de qual quadro veio a amostra
            var p = c switch { 'f' => 0.9f, 'm' => 0.4f, _ => 0.05f };
            if (segmenter.Push(frame, p) is { } segment) result.Add(segment);
        }
        if (segmenter.Flush() is { } last) result.Add(last);
        return result;
    }

    [Fact]
    public void SpeechFollowedBySilence_BecomesOneSegmentWithPadding()
    {
        // 10 quadros de silêncio, 20 de fala (640 ms), 15 de silêncio (480 ms >= 400 ms).
        var segments = Run(new string('.', 10) + new string('f', 20) + new string('.', 15));

        var s = Assert.Single(segments);
        Assert.False(s.ForcedCut);
        // Pré-roll de 160 ms = 5 quadros antes do primeiro quadro de fala (quadro 10).
        Assert.Equal(5 * Frame, s.StartSample);
        Assert.Equal(5f, s.Samples[0]);
        // Fim = último quadro de fala (29) + 96 ms de folga.
        Assert.Equal(30 * Frame + 96 * 16, s.EndSample);
        // A latência conta do fim da voz, sem a folga.
        Assert.Equal(30 * Frame, s.VoiceEndSample);
        // Confirmado depois de 13 quadros de silêncio (ceil(400/32)).
        Assert.Equal((30 + 13) * Frame, s.ConfirmedAtSample);
        Assert.Equal(13 * 32, s.ConfirmationDelay.TotalMilliseconds, 3);
    }

    [Fact]
    public void ShortPauseInsideSentence_DoesNotSplit()
    {
        // Pausa de 8 quadros (256 ms) no meio da frase é menor que os 400 ms exigidos.
        var segments = Run(new string('f', 15) + new string('.', 8) + new string('f', 15) + new string('.', 20));
        Assert.Single(segments);
    }

    [Fact]
    public void MiddleProbability_KeepsSpeechOpen_ButDoesNotStartIt()
    {
        // 'm' (0,4) está entre o limiar de fim (0,35) e o de início (0,5): mantém a fala, não abre.
        Assert.Empty(Run(new string('m', 30)));
        Assert.Single(Run(new string('f', 10) + new string('m', 30) + new string('.', 20)));
    }

    [Fact]
    public void Blip_ShorterThanMinimumSpeech_IsDiscarded()
    {
        // 5 quadros = 160 ms < 250 ms: estalo, passo, tiro.
        Assert.Empty(Run(new string('.', 10) + new string('f', 5) + new string('.', 20)));
    }

    [Fact]
    public void LongMonologue_IsCutAtMaximumLength()
    {
        var options = new SegmenterOptions { MaxSpeechMs = 2000 };
        var segments = Run(new string('f', 200) + new string('.', 20), options);

        Assert.True(segments.Count >= 3);
        Assert.All(segments.Take(segments.Count - 1), s => Assert.True(s.ForcedCut));
        Assert.All(segments, s => Assert.True(s.Duration.TotalMilliseconds <= 2000 + 32));
        // Os pedaços se encostam: nada da fala se perde entre um corte e outro.
        for (var i = 1; i < segments.Count; i++) Assert.Equal(segments[i - 1].EndSample, segments[i].StartSample);
    }

    [Fact]
    public void Flush_ClosesSpeechInProgress()
    {
        var s = Assert.Single(Run(new string('f', 20)));
        Assert.Equal(20 * Frame, s.EndSample);
    }

    [Fact]
    public void FrameSlicer_RegroupsArbitraryChunks()
    {
        var slicer = new FrameSlicer(4);
        var frames = new List<float[]>();
        frames.AddRange(slicer.Push([1, 2, 3]));
        frames.AddRange(slicer.Push([4, 5, 6, 7, 8, 9]));
        frames.AddRange(slicer.Push([10]));

        Assert.Equal(2, frames.Count);
        Assert.Equal([1f, 2, 3, 4], frames[0]);
        Assert.Equal([5f, 6, 7, 8], frames[1]);
    }
}
