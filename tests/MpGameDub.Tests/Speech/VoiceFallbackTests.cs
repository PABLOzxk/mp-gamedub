using MpGameDub.Core.Speech;

namespace MpGameDub.Tests.Speech;

public class VoiceFallbackTests
{
    [Fact]
    public async Task Placa_que_funciona_fica_e_a_cpu_nem_abre()
    {
        var gpu = new ScriptedVoice(_ => [0.2f, 0.4f]);
        var cpuOpened = false;

        var opened = await VoiceFallback.OpenAsync(() => gpu, () => { cpuOpened = true; return new ScriptedVoice(_ => [0.1f]); }, CancellationToken.None);

        Assert.Same(gpu, opened.Synthesizer);
        Assert.True(opened.OnGpu);
        Assert.Null(opened.Failure);
        Assert.False(cpuOpened);
        Assert.False(gpu.Disposed);
        Assert.Equal([VoiceFallback.ProbeText], gpu.Spoken);
    }

    [Fact]
    public async Task Placa_que_quebra_na_primeira_fala_vai_para_a_cpu()
    {
        // O que aconteceu na RTX 5060: a sessão do DirectML abre, e a primeira fala estoura dentro do motor.
        var gpu = new ScriptedVoice(_ => throw new InvalidOperationException("ConvTranspose E_INVALIDARG"));
        var cpu = new ScriptedVoice(_ => [0.1f]);

        var opened = await VoiceFallback.OpenAsync(() => gpu, () => cpu, CancellationToken.None);

        Assert.Same(cpu, opened.Synthesizer);
        Assert.False(opened.OnGpu);
        Assert.Contains("ConvTranspose", opened.Failure);
        Assert.True(gpu.Disposed);
        Assert.False(cpu.Disposed);
    }

    [Fact]
    public async Task Placa_que_nem_abre_vai_para_a_cpu()
    {
        var cpu = new ScriptedVoice(_ => [0.1f]);

        var opened = await VoiceFallback.OpenAsync(() => throw new InvalidOperationException("DirectML indisponível"), () => cpu, CancellationToken.None);

        Assert.Same(cpu, opened.Synthesizer);
        Assert.Equal("DirectML indisponível", opened.Failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Placa_que_devolve_audio_vazio_ou_nan_vai_para_a_cpu(bool nan)
    {
        var gpu = new ScriptedVoice(_ => nan ? [0.1f, float.NaN] : []);
        var cpu = new ScriptedVoice(_ => [0.1f]);

        var opened = await VoiceFallback.OpenAsync(() => gpu, () => cpu, CancellationToken.None);

        Assert.Same(cpu, opened.Synthesizer);
        Assert.Contains(nan ? "NaN" : "vazia", opened.Failure);
        Assert.True(gpu.Disposed);
    }

    [Fact]
    public async Task Cancelar_durante_o_teste_fecha_a_placa_e_nao_abre_a_cpu()
    {
        using var cts = new CancellationTokenSource();
        var gpu = new ScriptedVoice(_ => { cts.Cancel(); throw new OperationCanceledException(cts.Token); });
        var cpuOpened = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            VoiceFallback.OpenAsync(() => gpu, () => { cpuOpened = true; return new ScriptedVoice(_ => [0.1f]); }, cts.Token));

        Assert.True(gpu.Disposed);
        Assert.False(cpuOpened);
    }

    [Fact]
    public async Task Falta_de_memoria_ram_nao_e_mascarada()
    {
        var gpu = new ScriptedVoice(_ => throw new OutOfMemoryException());

        await Assert.ThrowsAsync<OutOfMemoryException>(() => VoiceFallback.OpenAsync(() => gpu, () => new ScriptedVoice(_ => [0.1f]), CancellationToken.None));
        Assert.True(gpu.Disposed);
    }

    private sealed class ScriptedVoice(Func<string, float[]> speak) : ISpeechSynthesizer
    {
        public string Name => "voz de teste";
        public List<string> Spoken { get; } = [];
        public bool Disposed { get; private set; }

        public Task<SynthesizedSpeech> SynthesizeAsync(string text, CancellationToken ct)
        {
            Spoken.Add(text);
            return Task.FromResult(new SynthesizedSpeech(speak(text), 24000));
        }

        public void Dispose() => Disposed = true;
    }
}
