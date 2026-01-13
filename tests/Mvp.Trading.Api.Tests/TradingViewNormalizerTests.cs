using System;
using Xunit;
using Mvp.Trading.Api;

namespace Mvp.Trading.Api.Tests
{
    public class TradingViewNormalizerTests
    {
        [Fact]
        public void Normalize_AcceptsFlatPascalCaseJson()
        {
            var json = @"{
  ""IdempotencyKey"": ""Pascal-1"",
  ""Ticker"": ""BTCUSD.P"",
  ""Exchange"": ""kraken"",
  ""Interval"": ""M1"",
  ""Close"": 93539.3,
  ""Volume"": null,
  ""DirectionHint"": ""UP"",
  ""SymbolHint"": ""BTCUSD.P"",
  ""Reason"": ""Test JSON""
}";
            var p = TradingViewNormalizer.Normalize(json);
            Assert.Equal("Pascal-1", p.IdempotencyKey);
            Assert.Equal("BTCUSD.P", p.Ticker);
            Assert.Equal(93539.3m, p.Close);
        }

        [Fact]
        public void Normalize_AcceptsFlatCamelCaseJson()
        {
            var json = @"{
  ""idempotencyKey"": ""camel-1"",
  ""ticker"": ""BTCUSD.P"",
  ""exchange"": ""kraken"",
  ""interval"": ""M1"",
  ""close"": 12345.67,
  ""volume"": null,
  ""directionHint"": ""UP"",
  ""symbolHint"": ""BTCUSD.P"",
  ""reason"": ""Test JSON""
}";
            var p = TradingViewNormalizer.Normalize(json);
            Assert.Equal("camel-1", p.IdempotencyKey);
            Assert.Equal(12345.67m, p.Close);
        }

        [Fact]
        public void Normalize_AcceptsNestedOhlcJson()
        {
            var json = @"{
  ""IdempotencyKey"": ""ohlc-1"",
  ""source"": ""tradingview"",
  ""exchange"": ""kraken"",
  ""ticker"": ""BTCUSD.P"",
  ""interval"": ""M1"",
  ""ohlc"": { ""open"":93000,""high"":93500,""low"":92000,""close"":93222 },
  ""volume"": 123.45,
  ""indicator"": { ""name"": ""ind"", ""values"": { ""main"": 1.23 } }
}";
            var p = TradingViewNormalizer.Normalize(json);
            Assert.Equal("ohlc-1", p.IdempotencyKey);
            Assert.Equal(93222m, p.Close);
            Assert.Equal(123.45m, p.Volume);
        }

        [Fact]
        public void Normalize_ThrowsOnInvalidJson()
        {
            var json = "{invalid-json}";
            Assert.Throws<InvalidOperationException>(() => TradingViewNormalizer.Normalize(json));
        }

        [Fact]
        public void Normalize_ThrowsWhenMissingIdempotency()
        {
            var json = @"{ ""ticker"": ""BTCUSD.P"", ""close"": 1.23 }";
            Assert.Throws<InvalidOperationException>(() => TradingViewNormalizer.Normalize(json));
        }
    }
}
