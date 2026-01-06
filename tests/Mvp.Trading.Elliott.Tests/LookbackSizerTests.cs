using System;
using Mvp.Trading.Contracts;
using Mvp.Trading.Elliott;
using Xunit;

namespace Mvp.Trading.Elliott.Tests;

/// <summary>
/// Tests for lookback sizing rules.
/// </summary>
public sealed class LookbackSizerTests
{
    [Fact]
    public void ComputeLookbackBars_ClampsToMin()
    {
        var options = new ElliottOptions
        {
            LookbackDays = 1,
            MinBars = 200,
            MaxBars = 5000
        };
        var result = LookbackSizer.ComputeLookbackBars(Timeframe.M15, 10, options);

        Assert.Equal(options.MinBars, result);
    }

    [Fact]
    public void ComputeLookbackBars_ClampsToMax()
    {
        var options = new ElliottOptions
        {
            LookbackDays = 10,
            MinBars = 1,
            MaxBars = 500
        };
        var result = LookbackSizer.ComputeLookbackBars(Timeframe.M1, 10, options);

        Assert.Equal(options.MaxBars, result);
    }

    [Fact]
    public void ComputeLookbackBars_ReturnsDayBasedValue()
    {
        var options = new ElliottOptions
        {
            LookbackDays = 1,
            MinBars = 1,
            MaxBars = 5000
        };
        var result = LookbackSizer.ComputeLookbackBars(Timeframe.M15, 10, options);

        Assert.Equal(96, result);
    }

    [Fact]
    public void ComputeLookbackBars_ReturnsLegacyDepthValue()
    {
        var options = new ElliottOptions
        {
            LookbackDays = 0,
            MinBars = 1,
            MaxBars = 5000
        };
        var result = LookbackSizer.ComputeLookbackBars(Timeframe.M15, 30, options);

        Assert.Equal(1100, result);
    }

    [Fact]
    public void ComputeLookbackBars_ThrowsOnInvalidDepth()
    {
        var options = new ElliottOptions();

        Assert.Throws<ArgumentOutOfRangeException>(() => LookbackSizer.ComputeLookbackBars(Timeframe.M15, 0, options));
    }
}
