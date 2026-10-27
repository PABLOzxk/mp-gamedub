namespace MpGameDub.Core.Pipeline;

public sealed record LatencySummary(int Count, double P50, double P95, double P99, double Max);

public static class LatencyStats
{
    /// <summary>Percentil com interpolação linear entre os vizinhos (o mesmo critério padrão do numpy).</summary>
    public static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0) return double.NaN;
        ArgumentOutOfRangeException.ThrowIfLessThan(percentile, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percentile, 100);
        var rank = percentile / 100 * (sortedValues.Count - 1);
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);
        return sortedValues[low] + (sortedValues[high] - sortedValues[low]) * (rank - low);
    }

    public static LatencySummary Summarize(IEnumerable<double> values)
    {
        var sorted = values.Where(v => !double.IsNaN(v)).OrderBy(v => v).ToList();
        if (sorted.Count == 0) return new LatencySummary(0, double.NaN, double.NaN, double.NaN, double.NaN);
        return new LatencySummary(sorted.Count, Percentile(sorted, 50), Percentile(sorted, 95), Percentile(sorted, 99), sorted[^1]);
    }
}
