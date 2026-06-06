using cAlgo.API;
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.Json;
using TradeKit.Core.PatternGeneration;

namespace TradeKit.CTrader.Indicators;

/// <summary>
/// Base class for Elliott Wave indicators providing common drawing and markup utilities.
/// Supports both v1 (<see cref="ElliottWaveExactMarkup"/>) and v2
/// (<see cref="ElliottWaveExactMarkupV2"/>) markup engines.
/// </summary>
public abstract class ElliottWaveIndicatorBase : Indicator
{
    /// <summary>
    /// Notation level used for the main (top-level) wave labels.
    /// case 4 = Minuette: impulse waves → (i) (ii) (iii) (iv) (v);
    ///                    corrective waves → (a) (b) (c) etc.
    /// </summary>
    protected const byte MAIN_NOTATION_LEVEL = 4;

    /// <summary>Maximum sub-wave depth to draw for v2 trees.</summary>
    protected const byte MAX_DRAW_DEPTH = 5;

    protected IBarsProvider BarProvider;
    protected ElliottWaveExactMarkup Markup;
    protected ElliottWaveExactMarkupV2 MarkupV2;
    protected bool UseV2 { get; set; }

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
            Color labelColor = GetWaveColor(node.ModelType);

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
    /// Max-point labels stack upward (bottom→top, youngest→oldest);
    /// min-point labels stack downward (top→bottom, youngest→oldest).
    /// </summary>
    protected void DrawStackedLabels(List<MarkupLabelItem> labels)
    {
        foreach (var grp in labels.GroupBy(x => x.BarIndex))
        {
            // Separate labels at maxima (wave going up → endpoint is peak)
            // from labels at minima (wave going down → endpoint is valley).
            var maxLabels = grp.Where(x => x.IsUp).OrderBy(x => x.NotationLevel).ToList();
            var minLabels = grp.Where(x => !x.IsUp).OrderBy(x => x.NotationLevel).ToList();

            // Max labels: common base = highest price, stack upward
            if (maxLabels.Count > 0)
            {
                double baseValue = maxLabels.Max(x => x.Value);
                double offset = 4.0;
                foreach (var item in maxLabels)
                {
                    Chart.DrawText(item.Name + "_t", item.LabelText,
                        item.BarIndex, baseValue + Symbol.PipSize * offset,
                        item.LabelColor);
                    offset += 10.0;
                }
            }

            // Min labels: common base = lowest price, stack downward
            if (minLabels.Count > 0)
            {
                double baseValue = minLabels.Min(x => x.Value);
                double offset = 4.0;
                foreach (var item in minLabels)
                {
                    Chart.DrawText(item.Name + "_t", item.LabelText,
                        item.BarIndex, baseValue - Symbol.PipSize * offset,
                        item.LabelColor);
                    offset += 10.0;
                }
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

    /// <summary>
    /// Converts a v2 <see cref="TreeNode"/> hierarchy into a v1-compatible
    /// <see cref="ExactParsedNode"/> tree for rendering.
    /// </summary>
    /// <param name="node">The v2 tree node (root or sub-wave).</param>
    /// <param name="segments">The zigzag segments from the v2 engine (used to infer IsUp).</param>
    /// <returns>An equivalent <see cref="ExactParsedNode"/> tree, or null if input is null.</returns>
    protected static ExactParsedNode ConvertV2NodeToExactParsedNode(
        TreeNode node,
        IReadOnlyList<ElliottWaveExactMarkupV2.Segment> segments)
    {
        if (node == null)
            return null;

        int waveCount = node.Children.Count;
        int expectedWaves = ElliottWaveExactMarkup.GetExpectedWaves(node.Model);

        // Infer IsUp from the children's segment range (fallback to pivot comparison).
        bool isUp;
        if (node.Children.Count > 0)
        {
            int firstChildStart = node.Children[0].RangeStartSegment;
            if (firstChildStart >= 0 && firstChildStart < segments.Count)
                isUp = segments[firstChildStart].IsUp;
            else
                isUp = node.EndPivot != null && node.StartPivot != null
                    && node.EndPivot.Value > node.StartPivot.Value;
        }
        else
        {
            isUp = node.EndPivot != null && node.StartPivot != null
                && node.EndPivot.Value > node.StartPivot.Value;
        }

        var subWaves = new ExactParsedNode[waveCount];
        for (int i = 0; i < waveCount; i++)
            subWaves[i] = ConvertV2NodeToExactParsedNode(node.Children[i], segments);

        // Calculate StartIndex/EndIndex from segment range.
        int startIndex = node.StartPivot?.BarIndex ?? 0;
        int endIndex = node.EndPivot?.BarIndex ?? 0;

        return new ExactParsedNode
        {
            ModelType = node.Model,
            WaveCount = waveCount,
            ExpectedWaves = expectedWaves,
            StartIndex = startIndex,
            EndIndex = endIndex,
            StartPoint = node.StartPivot,
            EndPoint = node.EndPivot,
            IsUp = isUp,
            Score = node.Score,
            SubWaves = subWaves,
            ActiveFromWaveIndex = node.ActiveFromWaveIndex,
        };
    }

    /// <summary>
    /// Draws all waves from a v2 <see cref="TreeNode"/> tree by first converting it
    /// to an <see cref="ExactParsedNode"/> and delegating to the existing drawing routines.
    /// </summary>
    protected void DrawV2MarkupNode(TreeNode node, IReadOnlyList<ElliottWaveExactMarkupV2.Segment> segments)
    {
        ExactParsedNode parsed = ConvertV2NodeToExactParsedNode(node, segments);
        if (parsed == null) return;
        DrawMarkupNode(parsed, "EWv2_", MAX_DRAW_DEPTH);
    }
}
