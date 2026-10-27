namespace MpGameDub.Core.Audio;

/// <summary>
/// Converte a taxa de amostragem de um sinal mono em pedaços, sem emendas entre um pedaço e outro.
/// Para reduzir a taxa (48 kHz do jogo para os 16 kHz do Whisper), passa antes por um filtro passa-baixa,
/// senão o que está acima de 8 kHz volta dobrado como ruído (aliasing). Depois interpola linearmente.
/// Qualidade de sobra para reconhecimento de fala; não é um conversor para ouvir música.
/// </summary>
public sealed class StreamingResampler
{
    private const int Taps = 63;

    private readonly double _step;
    private readonly float[]? _kernel;
    private readonly float[] _history = new float[Taps - 1];
    private float _previous;
    private bool _hasPrevious;
    private double _position;

    public StreamingResampler(int inputRate, int outputRate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(inputRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(outputRate, 1);
        InputRate = inputRate;
        OutputRate = outputRate;
        _step = (double)inputRate / outputRate;
        if (outputRate < inputRate) _kernel = LowPass(0.45 * outputRate / inputRate);
    }

    public int InputRate { get; }
    public int OutputRate { get; }

    public float[] Process(ReadOnlySpan<float> input)
    {
        if (input.IsEmpty) return [];
        if (InputRate == OutputRate) return input.ToArray();

        var filtered = _kernel is null ? input.ToArray() : Filter(input);
        var count = filtered.Length;
        var output = new List<float>((int)(count / _step) + 2);
        while (true)
        {
            var index = (int)Math.Floor(_position);
            if (index + 1 > count - 1) break;
            var frac = (float)(_position - index);
            var a = index < 0 ? (_hasPrevious ? _previous : filtered[0]) : filtered[index];
            var b = filtered[index + 1];
            output.Add(a + (b - a) * frac);
            _position += _step;
        }
        _position -= count;
        _previous = filtered[count - 1];
        _hasPrevious = true;
        return output.ToArray();
    }

    /// <summary>Converte um sinal inteiro de uma vez.</summary>
    public static float[] Convert(ReadOnlySpan<float> input, int inputRate, int outputRate) =>
        new StreamingResampler(inputRate, outputRate).Process(input);

    private float[] Filter(ReadOnlySpan<float> input)
    {
        var kernel = _kernel!;
        var history = _history.Length;
        var extended = new float[history + input.Length];
        _history.CopyTo(extended, 0);
        input.CopyTo(extended.AsSpan(history));

        var output = new float[input.Length];
        for (var i = 0; i < input.Length; i++)
        {
            var sum = 0f;
            var newest = i + history;
            for (var k = 0; k < Taps; k++) sum += kernel[k] * extended[newest - k];
            output[i] = sum;
        }
        extended.AsSpan(extended.Length - history).CopyTo(_history);
        return output;
    }

    // Sinc com janela de Blackman, normalizado para ganho 1 em frequência zero.
    private static float[] LowPass(double cutoff)
    {
        var kernel = new double[Taps];
        var middle = (Taps - 1) / 2.0;
        double sum = 0;
        for (var n = 0; n < Taps; n++)
        {
            var x = n - middle;
            var sinc = x == 0 ? 2 * cutoff : Math.Sin(2 * Math.PI * cutoff * x) / (Math.PI * x);
            var window = 0.42 - 0.5 * Math.Cos(2 * Math.PI * n / (Taps - 1)) + 0.08 * Math.Cos(4 * Math.PI * n / (Taps - 1));
            kernel[n] = sinc * window;
            sum += kernel[n];
        }
        return kernel.Select(k => (float)(k / sum)).ToArray();
    }
}
