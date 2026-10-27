using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using MpGameDub.Core.Speech;

namespace MpGameDub.Engines;

/// <summary>
/// Silero VAD 6 em ONNX. Cada chamada recebe 512 amostras de 16 kHz (32 ms) e devolve a probabilidade de fala.
/// O modelo guarda estado entre chamadas e espera as últimas 64 amostras do quadro anterior na frente do atual,
/// como faz o OnnxWrapper oficial do pacote silero-vad. Roda sempre na CPU: é minúsculo e não vale disputar a placa.
/// </summary>
public sealed class SileroVad : IVoiceActivityDetector
{
    private const int Context = 64;

    private readonly InferenceSession _session;
    private readonly float[] _input = new float[Context + 512];
    private float[] _state = new float[2 * 1 * 128];
    private readonly DenseTensor<long> _rate = new(new[] { (long)SpeechAudio.SampleRate }, []);

    public SileroVad(string modelPath)
    {
        using var options = OnnxSessions.Create(ComputeDevice.Cpu, cpuThreads: 1);
        _session = new InferenceSession(modelPath, options);
    }

    public int FrameSize => 512;

    public float Process(ReadOnlySpan<float> frame)
    {
        if (frame.Length != FrameSize) throw new ArgumentException($"O VAD espera quadros de {FrameSize} amostras.", nameof(frame));
        frame.CopyTo(_input.AsSpan(Context));

        var inputs = new[]
        {
            NamedOnnxValue.CreateFromTensor("input", new DenseTensor<float>(_input, [1, Context + 512])),
            NamedOnnxValue.CreateFromTensor("state", new DenseTensor<float>(_state, [2, 1, 128])),
            NamedOnnxValue.CreateFromTensor("sr", _rate),
        };
        using var results = _session.Run(inputs);
        var probability = results.First(r => r.Name == "output").AsEnumerable<float>().First();
        _state = results.First(r => r.Name == "stateN").AsEnumerable<float>().ToArray();

        // As últimas 64 amostras viram o contexto do próximo quadro.
        _input.AsSpan(_input.Length - Context).CopyTo(_input);
        return probability;
    }

    public void Reset()
    {
        Array.Clear(_input);
        _state = new float[2 * 1 * 128];
    }

    public void Dispose() => _session.Dispose();
}
