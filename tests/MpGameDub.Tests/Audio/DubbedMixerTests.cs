using MpGameDub.Core.Audio;
using MpGameDub.Core.Speech;

namespace MpGameDub.Tests.Audio;

public class DubbedMixerTests
{
    [Fact]
    public void Abaixa_o_jogo_so_enquanto_a_voz_toca_e_soma_a_voz_no_lugar_certo()
    {
        // Jogo: 4 s de 0,2 em estéreo a 8 kHz. Voz: 1 s de 0,1 a 8 kHz, começando em 2 s.
        const int rate = 8000;
        var game = new AudioClip(Enumerable.Repeat(0.2f, 4 * rate * 2).ToArray(), rate, 2);
        var voice = new SynthesizedSpeech(Enumerable.Repeat(0.1f, rate).ToArray(), rate);
        var ducking = new DuckingOptions { GameGain = 0.5f, Attack = TimeSpan.FromMilliseconds(100), Release = TimeSpan.FromMilliseconds(100) };

        var mixed = DubbedMixer.Mix(game, [(TimeSpan.FromSeconds(2), voice)], ducking);

        Assert.Equal(game.Samples.Length, mixed.Samples.Length);
        Assert.Equal(2, mixed.Channels);
        float At(double seconds, int channel) => mixed.Samples[(int)(seconds * rate) * 2 + channel];
        Assert.Equal(0.2f, At(1.0, 0), 4);                // longe da voz: intacto
        Assert.Equal(0.2f * 0.5f + 0.1f, At(2.5, 0), 4);  // durante: jogo pela metade + voz
        Assert.Equal(At(2.5, 0), At(2.5, 1), 6);           // voz nos dois canais
        Assert.InRange(At(1.95, 0), 0.1f, 0.2f);           // rampa de entrada
        Assert.InRange(At(3.05, 0), 0.1f, 0.2f);           // rampa de saída
        Assert.Equal(0.2f, At(3.5, 0), 4);                 // depois da rampa: intacto
    }

    [Fact]
    public void Voz_que_comeca_depois_do_fim_e_ignorada_e_soma_nao_estoura()
    {
        var game = new AudioClip(Enumerable.Repeat(0.9f, 16000).ToArray(), 16000, 1);
        var loud = new SynthesizedSpeech(Enumerable.Repeat(0.9f, 8000).ToArray(), 16000);

        var mixed = DubbedMixer.Mix(game, [(TimeSpan.FromSeconds(0.5), loud), (TimeSpan.FromSeconds(5), loud)], new DuckingOptions { GameGain = 1f });

        Assert.All(mixed.Samples, s => Assert.InRange(s, -1f, 1f));
        Assert.Equal(1f, mixed.Samples[12000]);
    }
}
