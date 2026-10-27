using Microsoft.ML.OnnxRuntime;

namespace MpGameDub.Engines;

/// <summary>Onde um motor roda. "Gpu" usa DirectML (qualquer placa com DirectX 12, sem instalar CUDA).</summary>
public enum ComputeDevice
{
    Cpu,
    Gpu,
}

/// <summary>Qual placa o DirectML usa, na ordem do DXGI. O programa escolhe uma vez, antes de criar os motores.</summary>
public static class DirectMlAdapter
{
    public static int Index { get; set; }
}

internal static class OnnxSessions
{
    public static SessionOptions Create(ComputeDevice device, int cpuThreads)
    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = cpuThreads,
            InterOpNumThreads = 1,
        };
        if (device == ComputeDevice.Gpu)
        {
            // Exigências do DirectML: sem padrão de memória e execução sequencial.
            options.EnableMemoryPattern = false;
            options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
            options.AppendExecutionProvider_DML(DirectMlAdapter.Index);
        }
        return options;
    }
}
