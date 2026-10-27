namespace MpGameDub.Core.Speech;

/// <summary>A voz aberta para a rodada. Failure diz por que a placa foi descartada, ou é null se ela foi usada.</summary>
public sealed record OpenedVoice(ISpeechSynthesizer Synthesizer, bool OnGpu, string? Failure);

/// <summary>
/// Abre a voz na placa e confere com uma fala de teste antes de usar. O DirectML pode abrir a sessão sem erro e só
/// quebrar na primeira fala (a RTX 5060 com o Kokoro quebra num ConvTranspose), ou devolver áudio vazio; nos dois
/// casos a voz vai para a CPU, em vez de derrubar o programa ou ficar muda a sessão inteira.
/// </summary>
public static class VoiceFallback
{
    public const string ProbeText = "Vamos.";

    public static async Task<OpenedVoice> OpenAsync(Func<ISpeechSynthesizer> onGpu, Func<ISpeechSynthesizer> onCpu, CancellationToken ct)
    {
        ISpeechSynthesizer? gpu = null;
        try
        {
            gpu = onGpu();
            var probe = await gpu.SynthesizeAsync(ProbeText, ct);
            if (probe.Samples.Length == 0) throw new InvalidOperationException("a fala de teste saiu vazia");
            if (probe.Samples.Any(float.IsNaN)) throw new InvalidOperationException("a fala de teste saiu com valores inválidos (NaN)");
            return new OpenedVoice(gpu, OnGpu: true, Failure: null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException && !ct.IsCancellationRequested)
        {
            gpu?.Dispose();
            return new OpenedVoice(onCpu(), OnGpu: false, Failure: ex.Message);
        }
        catch
        {
            gpu?.Dispose();
            throw;
        }
    }
}
