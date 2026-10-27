using MpGameDub.Core.Pipeline;

namespace MpGameDub.Tests.Pipeline;

public class LatencyStatsTests
{
    [Fact]
    public void Percentiles_InterpolateLikeNumpy()
    {
        var s = LatencyStats.Summarize([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        Assert.Equal(10, s.Count);
        Assert.Equal(5.5, s.P50, 6);
        Assert.Equal(9.55, s.P95, 6);
        Assert.Equal(9.91, s.P99, 6);
        Assert.Equal(10, s.Max);
    }

    [Fact]
    public void Empty_GivesNaN()
    {
        var s = LatencyStats.Summarize([]);
        Assert.Equal(0, s.Count);
        Assert.True(double.IsNaN(s.P50));
    }
}
