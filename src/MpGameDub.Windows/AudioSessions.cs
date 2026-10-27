using System.Diagnostics;
using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace MpGameDub.Windows;

/// <summary>Um programa tocando som agora, como aparece no Mixer de Volume do Windows.</summary>
public sealed record AudioSessionInfo(uint ProcessId, string ProcessName, string Title, float Peak, float Volume, bool Active);

/// <summary>Lista quem está tocando som na saída padrão. Só lê o que o Windows já mostra no Mixer; não toca no jogo.</summary>
[SupportedOSPlatform("windows")]
public static class AudioSessions
{
    public static IReadOnlyList<AudioSessionInfo> List()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var manager = device.AudioSessionManager;
        manager.RefreshSessions();
        var sessions = manager.Sessions;
        var result = new List<AudioSessionInfo>();
        var own = (uint)Environment.ProcessId;
        for (var i = 0; i < sessions.Count; i++)
        {
            using var session = sessions[i];
            if (session.IsSystemSoundsSession) continue;
            var pid = session.GetProcessID;
            if (pid == 0 || pid == own) continue;
            var (name, title) = Describe(pid);
            float peak = 0, volume = 1;
            try { peak = session.AudioMeterInformation.MasterPeakValue; } catch (NotSupportedException) { }
            try { volume = session.SimpleAudioVolume.Volume; } catch (NotSupportedException) { }
            result.Add(new AudioSessionInfo(pid, name, string.IsNullOrWhiteSpace(session.DisplayName) ? title : session.DisplayName, peak, volume,
                session.State == AudioSessionState.AudioSessionStateActive));
        }
        // Um processo pode ter várias sessões; mostra uma linha por processo, a mais alta.
        return result.GroupBy(s => s.ProcessId).Select(g => g.MaxBy(s => s.Peak)!).OrderByDescending(s => s.Active).ThenByDescending(s => s.Peak).ToList();
    }

    private static (string Name, string Title) Describe(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return (process.ProcessName, process.MainWindowTitle);
        }
        catch (ArgumentException)
        {
            return ($"processo {pid}", "");
        }
        catch (InvalidOperationException)
        {
            return ($"processo {pid}", "");
        }
    }
}
