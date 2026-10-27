using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MpGameDub.Windows;

/// <summary>O que o relatório precisa saber da máquina, lido do registro (sem WMI e sem administrador).</summary>
[SupportedOSPlatform("windows")]
public static class SystemInfo
{
    public static Dictionary<string, string> Describe()
    {
        var info = new Dictionary<string, string>
        {
            ["Windows"] = $"{RuntimeInformation.OSDescription} (build {Environment.OSVersion.Version.Build})",
            ["Processador"] = Read(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString")?.Trim() ?? "desconhecido",
            ["Núcleos lógicos"] = Environment.ProcessorCount.ToString(),
            ["Memória RAM"] = $"{GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1073741824.0:0.0} GB",
        };
        var gpus = Gpus();
        if (gpus.Count > 0) info["Placa de vídeo"] = string.Join(", ", gpus);
        return info;
    }

    public static bool SupportsProcessLoopback => Environment.OSVersion.Version.Build >= 20348;

    private static List<string> Gpus()
    {
        const string displayClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        var result = new List<string>();
        using var root = Registry.LocalMachine.OpenSubKey(displayClass);
        if (root is null) return result;
        foreach (var name in root.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
        {
            using var key = root.OpenSubKey(name);
            if (key?.GetValue("DriverDesc") is not string desc) continue;
            var memory = key.GetValue("HardwareInformation.qwMemorySize") is long bytes ? $" {bytes / 1073741824.0:0} GB" : "";
            var driver = key.GetValue("DriverVersion") is string v ? $", driver {v}" : "";
            result.Add(desc + memory + driver);
        }
        return result.Distinct().ToList();
    }

    private static string? Read(string path, string value)
    {
        using var key = Registry.LocalMachine.OpenSubKey(path);
        return key?.GetValue(value) as string;
    }
}
