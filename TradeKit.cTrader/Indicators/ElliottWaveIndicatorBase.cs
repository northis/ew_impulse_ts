using cAlgo.API;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.Json;
using TradeKit.Core.PatternGeneration;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Indicators;

/// <summary>
/// Base class for Elliott Wave indicators providing common drawing and markup utilities.
/// </summary>
public abstract class ElliottWaveIndicatorBase : Indicator
{
    /// <summary>
    /// Notation level used for the main (top-level) wave labels.
    /// case 4 = Minuette: impulse waves → (i) (ii) (iii) (iv) (v);
    ///                    corrective waves → (a) (b) (c) etc.
    /// </summary>
    protected const byte MAIN_NOTATION_LEVEL = 4;

    protected IBarsProvider BarProvider;
    protected ElliottWaveExactMarkup Markup;

    /// <summary>
    /// Attempts to load an <see cref="ExactParsedNode"/> from a JSON markup file,
    /// resolving bar indices from UTC timestamps.
    /// </summary>
    protected ExactParsedNode TryLoadMarkupFromFile(string filePath)
    {
        try
        {
            if (!System.IO.File.Exists(filePath))
                return null;

            string json = System.IO.File.ReadAllText(filePath);
            var jsonNode = JsonConvert.DeserializeObject<JsonMarkupNode>(json);
            return jsonNode?.ToParsedNode(BarProvider);
        }
        catch (Exception ex)
        {
            Print($"Markup file error: {ex.Message}");
            return null;
        }
    }

    /// <summary>Returns the cTrader chart color for an Elliott wave model type.</summary>
    protected static Color GetWaveColor(ElliottModelType modelType) => modelType switch
    {
        ElliottModelType.IMPULSE or ElliottModelType.SIMPLE_IMPULSE or
        ElliottModelType.DIAGONAL_CONTRACTING_INITIAL or ElliottModelType.DIAGONAL_CONTRACTING_ENDING or
        ElliottModelType.DIAGONAL_EXPANDING_INITIAL  or ElliottModelType.DIAGONAL_EXPANDING_ENDING
            => Color.FromHex("#3D85C6"),
        ElliottModelType.ZIGZAG or ElliottModelType.DOUBLE_ZIGZAG or ElliottModelType.TRIPLE_ZIGZAG
            => Color.FromHex("#FF9800"),
        ElliottModelType.TRIANGLE_CONTRACTING or ElliottModelType.TRIANGLE_EXPANDING or
        ElliottModelType.TRIANGLE_RUNNING
            => Color.FromHex("#787B86"),
        _ => Color.FromHex("#6AA84F")
    };

    protected record MarkupLabelItem(
        int BarIndex, double Value, bool IsUp,
        string Name, string LabelText,
        byte NotationLevel, Color LabelColor);

    /// <summary>
    /// Draws all wave lines and stacks labels at shared bar endpoints:
    /// youngest (innermost sub-wave) closest to the price bar, oldest furthest.
    /// </summary>
    protected void DrawMarkupNode(ExactParsedNode node, string prefix, byte notationLevel)
    {
        if (node == null || node.WaveCount == 0) return;
        var labels = new List<MarkupLabelItem>();
        DrawMarkupLines(node, prefix, notationLevel, labels);
        DrawStackedLabels(labels);
    }

    protected void DrawMarkupLines(
        ExactParsedNode node, string prefix, byte notationLevel,
        List<MarkupLabelItem> labels)
    {
        if (node == null || node.WaveCount == 0) return;

        NotationItem[] notation = TryGetNotation(node.ModelType, notationLevel);
        Color lineColor = GetWaveColor(node.ModelType);

        for (int i = 0; i < node.WaveCount; i++)
        {
            ExactParsedNode sw = node.SubWaves?[i];
            if (sw == null) continue;

            string labelText = (notation != null && i < notation.Length)
                ? notation[i].NotationKey
                : ElliottWaveExactMarkup.GetWaveKey(node.ModelType, i + 1);

            string name = $"{prefix}{sw.StartPoint.BarIndex}_{sw.EndPoint.BarIndex}_{labelText}";
            Color labelColor = GetWaveColor(
                sw.ModelType != ElliottModelType.SIMPLE_IMPULSE ? sw.ModelType : node.ModelType);

            Chart.DrawTrendLine(name + "_l",
                sw.StartPoint.BarIndex, sw.StartPoint.Value,
                sw.EndPoint.BarIndex,   sw.EndPoint.Value,
                lineColor, 1, LineStyle.Lines);

            labels.Add(new MarkupLabelItem(
                sw.EndPoint.BarIndex, sw.EndPoint.Value, sw.IsUp,
                name, labelText, notationLevel, labelColor));

            if (notationLevel > 0 && sw.ModelType != ElliottModelType.SIMPLE_IMPULSE)
                DrawMarkupLines(sw, prefix + "s_", (byte)(notationLevel - 1), labels);
        }
    }

    /// <summary>
    /// Groups collected label items by bar index and draws them vertically stacked,
    /// with the youngest wave label (lowest notation level) closest to the price bar.
    /// Each consecutive label is offset by 6 pips further from the bar.
    /// </summary>
    protected void DrawStackedLabels(List<MarkupLabelItem> labels)
    {
        foreach (var grp in labels.GroupBy(x => x.BarIndex))
        {
            var sorted = grp.OrderBy(x => x.NotationLevel).ToList();
            bool isUp  = sorted[0].IsUp;
            double sign   = isUp ? 1.0 : -1.0;
            double offset = 2.0;
            foreach (var item in sorted)
            {
                Chart.DrawText(item.Name + "_t", item.LabelText,
                    item.BarIndex, item.Value + sign * Symbol.PipSize * offset,
                    item.LabelColor);
                offset += 6.0;
            }
        }
    }

    /// <summary>
    /// Returns the notation items for the given model at the specified wave-degree level,
    /// or <c>null</c> when the model is not registered in <see cref="NotationHelper"/>.
    /// </summary>
    protected static NotationItem[] TryGetNotation(ElliottModelType model, byte level)
    {
        try { return NotationHelper.GetNotation(model, level); }
        catch { return null; }
    }
}
