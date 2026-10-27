using KokoroSharp;
using KokoroSharp.Core;
using KokoroSharp.Processing;
using MpGameDub.Core.Speech;

namespace MpGameDub.Engines;

/// <summary>
/// Voz Kokoro (82M) pelo motor do KokoroSharp, sem passar pelas classes de reprodução dele: elas usam o NAudio 2,
/// que não convive com o NAudio 3 da captura. Devolve PCM float de 24 kHz, mono, sem o silêncio das pontas.
/// </summary>
public sealed class KokoroSynthesizer : ISpeechSynthesizer
{
    public const int SampleRate = 24000;
    private const float SilenceFloor = 1e-4f;

    private readonly KokoroEngine _engine;
    private readonly KokoroVoice _voice;
    private readonly KokoroTTSPipelineConfig _config = new(new DefaultSegmentationConfig { MaxFirstSegmentLength = 510 });
    private readonly SemaphoreSlim _gate = new(1, 1);

    public KokoroSynthesizer(string modelPath, string voice, ComputeDevice device, int cpuThreads)
    {
        Name = $"Kokoro {voice} ({(device == ComputeDevice.Gpu ? "placa" : "CPU")})";
        _engine = new KokoroEngine(modelPath, OnnxSessions.Create(device, cpuThreads));
        _voice = KokoroVoiceManager.GetVoice(voice);
    }

    public string Name { get; }

    public async Task<SynthesizedSpeech> SynthesizeAsync(string text, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var tokens = Tokenizer.Tokenize(text.Trim(), _voice.GetLangCode(), _config.PreprocessText);
            var segments = _config.SegmentationFunc(tokens);
            if (segments.Count == 0) return new SynthesizedSpeech([], SampleRate);

            var output = new List<float>();
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var job = new GuardedJob(done)
            {
                Steps = segments.Select(s => new KokoroJob.KokoroJobStep(s, _voice.Features, _config.Speed, null)).ToList(),
            };
            for (var i = 0; i < job.Steps.Count; i++)
            {
                var step = job.Steps[i];
                var last = i == job.Steps.Count - 1;
                step.OnStepComplete = samples =>
                {
                    output.AddRange(Trim(samples));
                    if (last)
                    {
                        done.TrySetResult();
                    }
                    else if (step.Tokens.Length > 0 && Tokenizer.PunctuationTokens.Contains(step.Tokens[^1]))
                    {
                        var pause = _config.SecondsOfPauseBetweenProperSegments[Tokenizer.TokenToChar[step.Tokens[^1]]];
                        output.AddRange(new float[(int)(pause * SampleRate)]);
                    }
                };
            }

            _engine.EnqueueJob(job);
            await using (ct.Register(() => { job.Cancel(); done.TrySetCanceled(ct); }))
                await done.Task;
            return new SynthesizedSpeech(output.ToArray(), SampleRate);
        }
        finally
        {
            _gate.Release();
        }
    }

    // O Kokoro devolve um pouco de silêncio no começo e no fim; tirar o do começo adianta a voz.
    internal static ReadOnlySpan<float> Trim(float[] samples)
    {
        var start = 0;
        while (start < samples.Length && Math.Abs(samples[start]) <= SilenceFloor) start++;
        var end = samples.Length - 1;
        while (end > start && Math.Abs(samples[end]) <= SilenceFloor) end--;
        return start > end ? samples : samples.AsSpan(start, end - start + 1);
    }

    public void Dispose()
    {
        _engine.Dispose();
        _gate.Dispose();
    }

    /// <summary>
    /// O motor do KokoroSharp roda os trabalhos numa thread própria sem tratar erro: uma exceção ali (falta de
    /// memória na placa, por exemplo) derrubaria o programa inteiro. Aqui o erro vira falha desta fala só.
    /// </summary>
    private sealed class GuardedJob(TaskCompletionSource done) : KokoroJob
    {
        public override void Progress(KokoroModel model)
        {
            try
            {
                base.Progress(model);
            }
            catch (Exception ex)
            {
                Cancel();
                done.TrySetException(ex);
            }
        }
    }
}
