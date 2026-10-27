using System.Text.RegularExpressions;
using MpGameDub.Core.Audio;
using MpGameDub.Core.Models;
using MpGameDub.Core.Speech;

namespace MpGameDub.Engines.Tests;

public partial class KokoroAndWhisperTests
{
    [HeavyFact]
    public async Task Whisper_transcreve_o_jfk()
    {
        using var whisper = new WhisperRecognizer(await TestModels.GetAsync(ModelCatalog.WhisperSmall), "en", useGpu: false, cpuThreads: 4);
        var audio = TestModels.Jfk().ToMono().Resample(SpeechAudio.SampleRate).Samples;

        var transcript = await whisper.RecognizeAsync(audio, CancellationToken.None);

        Assert.Contains("ask not what your country can do for you", Normalize(transcript.Text));
    }

    [HeavyFact]
    public async Task Whisper_fecha_com_uma_fala_cancelada_ainda_saindo_do_whisper_cpp()
    {
        // O que derrubou o programa no PC do Pablo: o prazo da etapa estoura, o Whisper.net devolve o cancelamento na
        // hora, mas o whisper.cpp ainda está abortando a conta numa thread dele quando o programa fecha.
        var whisper = new WhisperRecognizer(await TestModels.GetAsync(ModelCatalog.WhisperSmall), "en", useGpu: false, cpuThreads: 1);
        var audio = TestModels.Jfk().ToMono().Resample(SpeechAudio.SampleRate).Samples;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => whisper.RecognizeAsync(audio, cts.Token));

        whisper.Dispose();
    }

    [HeavyFact]
    public async Task Whisper_cancelado_nao_trava_a_proxima_fala()
    {
        using var whisper = new WhisperRecognizer(await TestModels.GetAsync(ModelCatalog.WhisperSmall), "en", useGpu: false, cpuThreads: 4);
        var audio = TestModels.Jfk().ToMono().Resample(SpeechAudio.SampleRate).Samples;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => whisper.RecognizeAsync(audio, cts.Token));

        var transcript = await whisper.RecognizeAsync(audio, CancellationToken.None);

        Assert.Contains("ask not what your country can do for you", Normalize(transcript.Text));
    }

    [HeavyFact]
    public async Task Kokoro_fala_portugues_e_o_whisper_entende()
    {
        using var kokoro = new KokoroSynthesizer(await TestModels.GetAsync(ModelCatalog.Kokoro), "pf_dora", ComputeDevice.Cpu, cpuThreads: 4);
        var speech = await kokoro.SynthesizeAsync("Cuidado, os soldados estão vindo pela ponte.", CancellationToken.None);

        Assert.Equal(KokoroSynthesizer.SampleRate, speech.SampleRate);
        Assert.InRange(speech.Duration.TotalSeconds, 1.0, 8.0);
        Assert.True(AudioMath.Rms(speech.Samples) > 0.01f);

        using var whisper = new WhisperRecognizer(await TestModels.GetAsync(ModelCatalog.WhisperSmall), "pt", useGpu: false, cpuThreads: 4);
        var heard = await whisper.RecognizeAsync(StreamingResampler.Convert(speech.Samples, speech.SampleRate, SpeechAudio.SampleRate), CancellationToken.None);
        var text = Normalize(heard.Text);
        Assert.Contains("soldados", text);
        Assert.Contains("ponte", text);
    }

    [HeavyFact]
    public async Task Kokoro_cancelado_nao_trava_a_proxima_fala()
    {
        using var kokoro = new KokoroSynthesizer(await TestModels.GetAsync(ModelCatalog.Kokoro), "pm_alex", ComputeDevice.Cpu, cpuThreads: 4);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => kokoro.SynthesizeAsync("Primeira fala.", cts.Token));

        var speech = await kokoro.SynthesizeAsync("Segunda fala.", CancellationToken.None);
        Assert.True(speech.Samples.Length > 0);
    }

    private static string Normalize(string text) => Spaces().Replace(NotLetters().Replace(text.ToLowerInvariant(), " "), " ").Trim();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NotLetters();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}

public class KokoroTrimTests
{
    [Fact]
    public void Tira_o_silencio_das_pontas_e_mantem_o_do_meio()
    {
        var samples = new float[] { 0, 0.00005f, 0.3f, 0, 0, -0.2f, 0, 0 };
        Assert.Equal(new[] { 0.3f, 0, 0, -0.2f }, KokoroSynthesizer.Trim(samples).ToArray());
    }

    [Fact]
    public void Audio_todo_em_silencio_volta_inteiro()
    {
        var samples = new float[4];
        Assert.Equal(4, KokoroSynthesizer.Trim(samples).Length);
    }
}

public class KokoroSpeedTests
{
    [HeavyFact]
    public async Task Fala_curta_sai_em_portugues_com_duracao_plausivel()
    {
        using var kokoro = new KokoroSynthesizer(await TestModels.GetAsync(ModelCatalog.Kokoro), "pf_dora", ComputeDevice.Cpu, cpuThreads: 4);
        var speech = await kokoro.SynthesizeAsync("Recarregando! Me dá cobertura.", CancellationToken.None);
        Assert.InRange(speech.Duration.TotalSeconds, 1.0, 5.0);
        Assert.True(Math.Abs(speech.Samples[0]) > 1e-4f);
    }
}

public class KokoroConcurrencyTests
{
    // O CI quebrou com duas vozes fonemizando ao mesmo tempo pela primeira vez: o KokoroSharp guarda o fonemizador
    // de cada idioma e a lista de vozes em coleções estáticas sem trava. O teste cria as vozes juntas e limpa o
    // cache do fonemizador antes de cada rodada, para repetir a "primeira vez".
    [HeavyFact]
    public async Task Vozes_em_paralelo_na_primeira_fala_nao_corrompem_o_cache_do_kokoro()
    {
        var model = await TestModels.GetAsync(ModelCatalog.Kokoro);
        var cache = typeof(KokoroSharp.Processing.Tokenizer).GetField("espeakG2Ps", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.GetValue(null) as System.Collections.IDictionary;
        Assert.NotNull(cache);
        string[] lines = ["Recarregando!", "Me dá cobertura.", "Eles estão vindo.", "Abaixa!"];

        for (var round = 0; round < 3; round++)
        {
            var voices = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => Task.Run(() =>
                new KokoroSynthesizer(model, i % 2 == 0 ? "pf_dora" : "pm_alex", ComputeDevice.Cpu, cpuThreads: 1))));
            try
            {
                lock (KokoroSynthesizer.SharedState) cache.Clear();
                var speech = await Task.WhenAll(voices.Select((v, i) => Task.Run(() => v.SynthesizeAsync(lines[i], CancellationToken.None))));
                Assert.All(speech, s => Assert.True(s.Samples.Length > 0));
            }
            finally
            {
                foreach (var v in voices) v.Dispose();
            }
        }
    }
}
