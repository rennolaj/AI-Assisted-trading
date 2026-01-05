using System.Linq;
using Mvp.Trading.Indicators;
using Xunit;

namespace Mvp.Trading.Indicators.Tests;

/// <summary>
/// Tests for default indicator configurations.
/// </summary>
public sealed class IndicatorDefaultsTests
{
    [Theory]
    [InlineData("swing")]
    [InlineData("swing_default")]
    public void ForMode_ReturnsSwingConfig(string mode)
    {
        var config = IndicatorDefaults.ForMode(mode);

        Assert.Equal(IndicatorDefaults.SwingMode, config.Mode);
        Assert.Contains(config.AnchorTimeframe, config.Timeframes);
        Assert.Contains(config.TrendTimeframe, config.Timeframes);
    }
}
