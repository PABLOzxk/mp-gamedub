using System.Diagnostics;
using MpGameDub.Core.Pipeline;

namespace MpGameDub.Tests.Pipeline;

public class GameExitTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(50);

    [Fact]
    public async Task Processo_que_fecha_e_percebido()
    {
        // Um "jogo" de mentira que fica aberto até o teste fechar.
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("ping", "-n 60 127.0.0.1")
            : new ProcessStartInfo("sleep", "60");
        start.RedirectStandardOutput = true;
        using var game = Process.Start(start)!;
        var name = game.ProcessName;
        Assert.True(GameExit.IsRunning(game.Id, name));

        var gone = GameExit.WaitAsync(game.Id, name, Interval, CancellationToken.None);
        await Task.Delay(200);
        Assert.False(gone.IsCompleted);

        game.Kill();
        await gone.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(GameExit.IsRunning(game.Id, name));
    }

    [Fact]
    public async Task Processo_aberto_nao_e_dado_como_fechado()
    {
        using var self = Process.GetCurrentProcess();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GameExit.WaitAsync(self.Id, self.ProcessName, Interval, cts.Token));
    }

    [Fact]
    public void Mesmo_numero_com_outro_nome_e_outro_programa()
    {
        // O Windows pode dar o número do jogo que fechou a outro programa: isso não é o jogo de volta.
        Assert.False(GameExit.IsRunning(Environment.ProcessId, "Bully-que-ja-fechou"));
    }
}
