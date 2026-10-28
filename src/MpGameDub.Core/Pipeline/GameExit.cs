using System.Diagnostics;

namespace MpGameDub.Core.Pipeline;

/// <summary>
/// Percebe quando o jogo fecha. Só olha a lista de processos do sistema, como o Gerenciador de Tarefas, sem abrir
/// nada no processo do jogo. Confere o nome junto com o número, porque o Windows reaproveita números de processo.
/// </summary>
public static class GameExit
{
    /// <summary>Termina quando o processo some da lista, conferindo a cada intervalo.</summary>
    public static async Task WaitAsync(int processId, string processName, TimeSpan interval, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);
        while (IsRunning(processId, processName)) await timer.WaitForNextTickAsync(ct);
    }

    public static bool IsRunning(int processId, string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            return processes.Any(p => p.Id == processId);
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }
}
