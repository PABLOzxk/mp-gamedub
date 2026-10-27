using System.Text.Json;
using MpGameDub.Core.Models;
using MpGameDub.Core.Speech;

namespace MpGameDub.Engines.Tests;

public class SileroVadTests
{
    // Probabilidades geradas pelo OnnxWrapper oficial do pacote silero-vad (Python) no mesmo arquivo, quadro a quadro.
    [Fact]
    public async Task Bate_com_a_implementacao_oficial_no_audio_do_jfk()
    {
        var expected = JsonSerializer.Deserialize<float[]>(File.ReadAllText(TestModels.Fixture("jfk-silero-6.2.3.json")))!;
        using var vad = new SileroVad(await TestModels.GetAsync(ModelCatalog.SileroVad));
        var audio = TestModels.Jfk().ToMono().Resample(SpeechAudio.SampleRate).Samples;

        var frames = audio.Length / vad.FrameSize;
        Assert.Equal(expected.Length, frames);
        for (var i = 0; i < frames; i++)
        {
            var p = vad.Process(audio.AsSpan(i * vad.FrameSize, vad.FrameSize));
            Assert.True(Math.Abs(p - expected[i]) < 2e-3, $"Quadro {i}: {p} em vez de {expected[i]}");
        }
    }

    [Fact]
    public async Task Reset_recomeca_do_zero()
    {
        using var vad = new SileroVad(await TestModels.GetAsync(ModelCatalog.SileroVad));
        var audio = TestModels.Jfk().ToMono().Resample(SpeechAudio.SampleRate).Samples;
        var first = Run(vad, audio, 40);
        vad.Reset();
        var second = Run(vad, audio, 40);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Segmentador_acha_a_fala_do_jfk()
    {
        using var vad = new SileroVad(await TestModels.GetAsync(ModelCatalog.SileroVad));
        var audio = TestModels.Jfk().ToMono().Resample(SpeechAudio.SampleRate).Samples;
        var segmenter = new SpeechSegmenter(vad.FrameSize, new SegmenterOptions());
        var segments = new List<SpeechSegment>();
        for (var i = 0; i + vad.FrameSize <= audio.Length; i += vad.FrameSize)
        {
            var frame = audio.AsSpan(i, vad.FrameSize).ToArray();
            if (segmenter.Push(frame, vad.Process(frame)) is { } segment) segments.Add(segment);
        }
        if (segmenter.Flush() is { } last) segments.Add(last);

        Assert.NotEmpty(segments);
        Assert.InRange(segments[0].Start.TotalSeconds, 0.0, 0.6);
        Assert.InRange(segments[^1].End.TotalSeconds, 9.5, 11.1);
        Assert.All(segments, s => Assert.False(s.ForcedCut));
    }

    // assets/exemplo.wav: 9 falas com ruído, passos, música baixa e uma explosão por cima da sexta fala.
    [Fact]
    public async Task Exemplo_do_programa_tem_as_nove_falas_e_o_ruido_nao_vira_fala()
    {
        using var vad = new SileroVad(await TestModels.GetAsync(ModelCatalog.SileroVad));
        var audio = MpGameDub.Core.Audio.WavFile.Read(TestModels.Fixture("exemplo.wav")).Resample(SpeechAudio.SampleRate).Samples;
        var segmenter = new SpeechSegmenter(vad.FrameSize, new SegmenterOptions());
        var segments = new List<SpeechSegment>();
        for (var i = 0; i + vad.FrameSize <= audio.Length; i += vad.FrameSize)
        {
            var frame = audio.AsSpan(i, vad.FrameSize).ToArray();
            if (segmenter.Push(frame, vad.Process(frame)) is { } segment) segments.Add(segment);
        }
        if (segmenter.Flush() is { } last) segments.Add(last);

        double[] starts = [1.50, 6.03, 11.16, 15.56, 21.20, 26.81, 29.94, 35.81, 41.43];
        Assert.Equal(starts.Length, segments.Count);
        for (var i = 0; i < starts.Length; i++)
            Assert.InRange(segments[i].Start.TotalSeconds, starts[i] - 0.4, starts[i] + 0.2);
    }

    private static float[] Run(SileroVad vad, float[] audio, int frames) =>
        Enumerable.Range(0, frames).Select(i => vad.Process(audio.AsSpan(i * vad.FrameSize, vad.FrameSize))).ToArray();
}
