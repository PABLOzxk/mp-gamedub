using System.Diagnostics;
using System.Runtime.Versioning;
using MpGameDub.Core.Reports;

namespace MpGameDub.Windows;

public sealed record ResourceSample(double? VramMb, double RamMb, double CpuPercent, double? GpuPercent);

/// <summary>
/// Mede, uma vez por segundo, quanto o próprio MP GameDub usa de memória da placa, RAM, processador e placa.
/// Usa os contadores que o Gerenciador de Tarefas usa ("GPU Process Memory" e "GPU Engine"), que não pedem
/// administrador. Se o Windows não tiver os contadores, a medição da placa fica em branco e o resto segue.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ResourceMonitor : IDisposable
{
    private readonly Process _self = Process.GetCurrentProcess();
    private readonly List<ResourceSample> _samples = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private readonly string _pidTag = $"pid_{Environment.ProcessId}_";

    public ResourceMonitor()
    {
        _loop = Task.Run(() => LoopAsync(_stop.Token));
    }

    public string? GpuCountersProblem { get; private set; }

    public ResourceSample? Last
    {
        get { lock (_samples) return _samples.Count > 0 ? _samples[^1] : null; }
    }

    public ResourceSummary Summarize()
    {
        lock (_samples)
        {
            if (_samples.Count == 0) return new ResourceSummary(null, null, null, null);
            var vram = _samples.Where(s => s.VramMb is not null).Select(s => s.VramMb!.Value).DefaultIfEmpty(double.NaN).Max();
            var gpu = _samples.Where(s => s.GpuPercent is not null).Select(s => s.GpuPercent!.Value).ToList();
            return new ResourceSummary(
                double.IsNaN(vram) ? null : vram,
                _samples.Max(s => s.RamMb),
                _samples.Average(s => s.CpuPercent),
                gpu.Count > 0 ? gpu.Average() : null);
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        var lastCpu = _self.TotalProcessorTime;
        var lastTime = Stopwatch.GetTimestamp();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await WaitAsync(timer, ct))
        {
            _self.Refresh();
            var cpu = _self.TotalProcessorTime;
            var now = Stopwatch.GetTimestamp();
            var elapsed = Stopwatch.GetElapsedTime(lastTime, now).TotalMilliseconds;
            var cpuPercent = elapsed > 0 ? (cpu - lastCpu).TotalMilliseconds / elapsed / Environment.ProcessorCount * 100 : 0;
            lastCpu = cpu;
            lastTime = now;

            var sample = new ResourceSample(ReadVram(), _self.WorkingSet64 / 1048576.0, cpuPercent, ReadGpu());
            lock (_samples) _samples.Add(sample);
        }
    }

    private double? ReadVram()
    {
        try
        {
            var category = new PerformanceCounterCategory("GPU Process Memory");
            double total = 0;
            var found = false;
            foreach (var instance in category.GetInstanceNames().Where(n => n.StartsWith(_pidTag, StringComparison.Ordinal)))
            {
                using var counter = new PerformanceCounter("GPU Process Memory", "Dedicated Usage", instance, readOnly: true);
                total += counter.RawValue;
                found = true;
            }
            return found ? total / 1048576.0 : 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            GpuCountersProblem = ex.Message;
            return null;
        }
    }

    // Soma o uso de todos os motores da placa (3D, Compute, Copy) deste processo, como o Gerenciador de Tarefas.
    private readonly Dictionary<string, PerformanceCounter> _engines = [];

    private double? ReadGpu()
    {
        try
        {
            var category = new PerformanceCounterCategory("GPU Engine");
            var instances = category.GetInstanceNames().Where(n => n.StartsWith(_pidTag, StringComparison.Ordinal)).ToHashSet();
            foreach (var gone in _engines.Keys.Where(k => !instances.Contains(k)).ToList())
            {
                _engines[gone].Dispose();
                _engines.Remove(gone);
            }
            double busiest = 0;
            var byType = new Dictionary<string, double>();
            foreach (var instance in instances)
            {
                if (!_engines.TryGetValue(instance, out var counter))
                {
                    counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, readOnly: true);
                    counter.NextValue(); // o primeiro valor de um contador de taxa é sempre 0
                    _engines[instance] = counter;
                    continue;
                }
                var type = instance[(instance.LastIndexOf("engtype_", StringComparison.Ordinal) + 8)..];
                byType[type] = byType.GetValueOrDefault(type) + counter.NextValue();
            }
            foreach (var value in byType.Values) busiest = Math.Max(busiest, value);
            return Math.Min(100, busiest);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            GpuCountersProblem = ex.Message;
            return null;
        }
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _loop.Wait(TimeSpan.FromSeconds(3)); } catch (AggregateException) { }
        foreach (var counter in _engines.Values) counter.Dispose();
        _stop.Dispose();
        _self.Dispose();
    }
}
