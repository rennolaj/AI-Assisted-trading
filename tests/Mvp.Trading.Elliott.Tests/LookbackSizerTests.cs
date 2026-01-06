using System;
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
        var options = new ElliottOptions();
        var result = LookbackSizer.ComputeLookbackBars(10, options);

        Assert.Equal(options.MinBars, result);
    }

    [Fact]
    public void ComputeLookbackBars_ClampsToMax()
    {
        var options = new ElliottOptions();
        var result = LookbackSizer.ComputeLookbackBars(200, options);

        Assert.Equal(options.MaxBars, result);
    }

    [Fact]
    public void ComputeLookbackBars_ReturnsComputedValue()
    {
        var options = new ElliottOptions();
        var result = LookbackSizer.ComputeLookbackBars(30, options);

        Assert.Equal(1100, result);
    }

    [Fact]
    public void ComputeLookbackBars_ThrowsOnInvalidDepth()
    {
        var options = new ElliottOptions();

        Assert.Throws<ArgumentOutOfRangeException>(() => LookbackSizer.ComputeLookbackBars(0, options));
    }
}
