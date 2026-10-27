using System.Runtime.Versioning;
using System.Text.Json;
using NAudio.CoreAudioApi;

namespace MpGameDub.Windows;

/// <summary>
/// Abaixa e devolve o volume do jogo no Mixer do Windows (o mesmo controle que o usuário mexe à mão).
/// O volume original fica salvo num arquivo antes de mexer: se o programa fechar no meio, a próxima abertura
/// devolve o volume, e o jogo não fica baixo para sempre.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GameVolume : IDisposable
{
    private readonly uint _processId;
    private readonly string _stateFile;
    private float? _original;
    private bool _ducked;

    public GameVolume(uint processId, string stateFile)
    {
        _processId = processId;
        _stateFile = stateFile;
    }

    /// <summary>Devolve o volume de uma sessão anterior que fechou sem devolver.</summary>
    public static string? RestorePending(string stateFile)
    {
        if (!File.Exists(stateFile)) return null;
        try
        {
            var saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(stateFile));
            if (saved is null) return null;
            var restored = SetVolume(saved.ProcessId, _ => saved.Volume);
            return restored ? $"Volume do processo {saved.ProcessId} devolvido para {saved.Volume:P0}." : null;
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            File.Delete(stateFile);
        }
    }

    public void Duck(float gain)
    {
        if (_ducked) return;
        _original ??= ReadVolume(_processId);
        if (_original is not { } original) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_stateFile)!);
        File.WriteAllText(_stateFile, JsonSerializer.Serialize(new Saved(_processId, original)));
        SetVolume(_processId, _ => original * gain);
        _ducked = true;
    }

    public void Restore()
    {
        if (!_ducked || _original is not { } original) return;
        SetVolume(_processId, _ => original);
        _ducked = false;
        File.Delete(_stateFile);
    }

    public void Dispose() => Restore();

    private static float? ReadVolume(uint pid)
    {
        float? volume = null;
        ForEachSession(pid, s => volume ??= s.SimpleAudioVolume.Volume);
        return volume;
    }

    private static bool SetVolume(uint pid, Func<float, float> change)
    {
        var any = false;
        ForEachSession(pid, s =>
        {
            s.SimpleAudioVolume.Volume = Math.Clamp(change(s.SimpleAudioVolume.Volume), 0f, 1f);
            any = true;
        });
        return any;
    }

    private static void ForEachSession(uint pid, Action<AudioSessionControl> action)
    {
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device)
            {
                var manager = device.AudioSessionManager;
                manager.RefreshSessions();
                var sessions = manager.Sessions;
                for (var i = 0; i < sessions.Count; i++)
                {
                    using var session = sessions[i];
                    if (session.GetProcessID != pid) continue;
                    try { action(session); } catch (NotSupportedException) { }
                }
            }
        }
    }

    private sealed record Saved(uint ProcessId, float Volume);
}
