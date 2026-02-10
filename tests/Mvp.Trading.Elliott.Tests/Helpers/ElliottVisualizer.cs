using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Mvp.Trading.Contracts;

namespace Mvp.Trading.Elliott.Tests.Helpers;

/// <summary>
/// Small test helper to emit an HTML report with embedded Plotly for quick visual inspection.
/// Writes a self-contained HTML file to the given path.
/// </summary>
public static class ElliottVisualizer
{
    public static void WriteReport(
        string filePath,
        string symbol,
        IReadOnlyList<Candle> candles,
        IReadOnlyList<PivotPoint> pivots,
        ElliottCandidates candidates)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");

        // Prepare OHLC arrays
        var x = candles.Select(c => c.OpenTimeUtc.ToString("o")).ToArray();
        var open = candles.Select(c => c.Open).ToArray();
        var high = candles.Select(c => c.High).ToArray();
        var low = candles.Select(c => c.Low).ToArray();
        var close = candles.Select(c => c.Close).ToArray();

        var pivotsJson = JsonSerializer.Serialize(pivots.Select(p => new
        {
            index = p.Index,
            time = p.TimeUtc.ToString("o"),
            price = p.Price,
            type = p.Type.ToString()
        }));

        var candidatesJson = JsonSerializer.Serialize(candidates.Candidates.Select(c => new
        {
            id = c.CandidateId,
            label = c.WaveLabel,
            score = c.Score,
            confidence = c.Confidence,
            violations = c.RuleViolations.Select(v => $"{v.Rule} ({v.Severity}): {v.Details}").ToArray()
        }));

        // Build candidate summary table
        var tableRows = new StringBuilder();
        foreach (var c in candidates.Candidates)
        {
            var violations = c.RuleViolations.Count > 0
                ? string.Join("<br/>", c.RuleViolations.Select(v => $"<code>{v.Rule}</code> ({v.Severity}): {v.Details}"))
                : "<span style='color:green'>None</span>";
            var scoreColor = c.Score > 0.5m ? "green" : c.Score > 0 ? "orange" : "red";
            tableRows.AppendLine(
                $"<tr><td><code>{c.CandidateId[..Math.Min(12, c.CandidateId.Length)]}</code></td>" +
                $"<td>{c.WaveLabel}</td><td>{c.PatternType}</td>" +
                $"<td style='color:{scoreColor}'>{c.Score:F3}</td>" +
                $"<td>{c.Confidence:F3}</td>" +
                $"<td>{c.Invalidation.LongInvalidationPrice?.ToString("F2") ?? "—"}</td>" +
                $"<td>{c.Invalidation.ShortInvalidationPrice?.ToString("F2") ?? "—"}</td>" +
                $"<td>{violations}</td></tr>");
        }

        var xJson = JsonSerializer.Serialize(x);
        var openJson = JsonSerializer.Serialize(open);
        var highJson = JsonSerializer.Serialize(high);
        var lowJson = JsonSerializer.Serialize(low);
        var closeJson = JsonSerializer.Serialize(close);

        var html = $@"<!doctype html>
<html>
  <head>
    <meta charset=""utf-8"" />
    <title>Elliott Visual Report — {symbol}</title>
    <script src=""https://cdn.plot.ly/plotly-2.35.2.min.js""></script>
    <style>
      body {{ font-family: system-ui, -apple-system, 'Segoe UI', Roboto, 'Helvetica Neue', Arial; margin: 1em; }}
      table {{ border-collapse: collapse; width: 100%; margin-top: 1em; }}
      th, td {{ border: 1px solid #ccc; padding: 6px 10px; text-align: left; font-size: 0.9em; }}
      th {{ background: #f5f5f5; }}
      .summary {{ display: flex; gap: 2em; margin-top: 0.5em; }}
      .summary span {{ font-weight: bold; }}
    </style>
  </head>
  <body>
    <h2>Elliott Visual Report — {symbol}</h2>
    <div class=""summary"">
      <span>Candles: {candles.Count}</span>
      <span>Pivots: {pivots.Count}</span>
      <span>Candidates: {candidates.Candidates.Count}</span>
      <span>Timeframe: {candidates.BaseTimeframe}</span>
    </div>
    <div id=""plot"" style=""width:100%;height:600px;""></div>

    <h3>Candidates</h3>
    <table>
      <thead><tr>
        <th>ID</th><th>Wave</th><th>Pattern</th><th>Score</th><th>Confidence</th>
        <th>Long Inval.</th><th>Short Inval.</th><th>Violations</th>
      </tr></thead>
      <tbody>{tableRows}</tbody>
    </table>

    <script>
      const x = {xJson};
      const open = {openJson};
      const high = {highJson};
      const low = {lowJson};
      const close = {closeJson};

      const pivots = {pivotsJson};
      const candidates = {candidatesJson};

      const ohlc = {{
        x: x,
        open: open,
        high: high,
        low: low,
        close: close,
        increasing: {{line: {{color: '#26a69a'}}}},
        decreasing: {{line: {{color: '#ef5350'}}}},
        type: 'candlestick',
        xaxis: 'x',
        yaxis: 'y',
        name: 'OHLC'
      }};

      const pivotTrace = {{
        x: pivots.map(p => p.time),
        y: pivots.map(p => p.price),
        mode: 'markers+text',
        marker: {{ size: 10, symbol: pivots.map(p => p.type === 'High' ? 'triangle-down' : 'triangle-up'), color: pivots.map(p => p.type === 'High' ? '#ef5350' : '#1e88e5') }},
        text: pivots.map((p, i) => 'P' + i),
        textposition: pivots.map(p => p.type === 'High' ? 'top center' : 'bottom center'),
        name: 'Pivots',
        hovertemplate: '%{{text}}<br>%{{y:.2f}}<br>%{{x}}<extra></extra>'
      }};

      const layout = {{
        title: 'Elliott Report — {symbol}',
        xaxis: {{ rangeslider: {{ visible: false }}, title: 'Time' }},
        yaxis: {{ title: 'Price' }},
        height: 600,
        margin: {{ l: 60, r: 20, t: 40, b: 40 }}
      }};

      Plotly.newPlot('plot', [ohlc, pivotTrace], layout);
    </script>
  </body>
</html>
";

        File.WriteAllText(filePath, html);
    }
}
