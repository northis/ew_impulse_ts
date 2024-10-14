using Microsoft.FSharp.Core;
using Newtonsoft.Json;
using Plotly.NET;
using Plotly.NET.ImageExport;
using Plotly.NET.LayoutObjects;
using TradeKit.Core.Json;
using TradeKit.Core.PatternGeneration;
using static Plotly.NET.StyleParam;

namespace TradeKit.Core.Common
{
    public static class ChartGenerator
    {
        public static readonly Color SHORT_COLOR = Color.fromHex("#EF5350");
        public static readonly Color LONG_COLOR = Color.fromHex("#26A69A");
        public static readonly Color BLACK_COLOR = Color.fromARGB(255, 22, 26, 37);
        public static readonly Color WHITE_COLOR = Color.fromARGB(240, 209, 212, 220);
        public static readonly Color SEMI_WHITE_COLOR = Color.fromARGB(80, 209, 212, 220);
        public const double CHART_FONT_MAIN = 24;

        public static GenericChart GetCandlestickChart(
            List<JsonCandleExport> candles,
            string name)
        {
            double[] o = new double[candles.Count];
            double[] h = new double[candles.Count];
            double[] l = new double[candles.Count];
            double[] c = new double[candles.Count];
            DateTime[] d = new DateTime[candles.Count];
            
            for (int i = 0; i < candles.Count; i++)
            {
                o[i] = candles[i].O;
                h[i] = candles[i].H;
                c[i] = candles[i].C;
                l[i] = candles[i].L;
                d[i] = candles[i].OpenDate;
            }

            GenericChart res = GetCandlestickChart(o, h, l, c, d, name);
            return res;
        }

        public static GenericChart GetCandlestickChart(
            double[] o,
            double[] h,
            double[] l,
            double[] c,
            DateTime[] d,
            string name,
            List<DateTime> rangeBreaks, 
            TimeSpan timeFrameTimeSpan,
            out Rangebreak[] rbs)
        {
            FSharpOption<int> dValue = (int)timeFrameTimeSpan.TotalMilliseconds;
            rbs = new []
            {
                Rangebreak.init<string, string>(rangeBreaks.Any(),
                    DValue: dValue,
                    Values: rangeBreaks.Select(a => a.ToString("O")).ToFSharp())
            };

            GenericChart res = 
                GetCandlestickChart(o, h, l, c, d, name, rbs);
            return res;
        }

        public static Annotation GetAnnotation(
            DateTime x, double y, Color textColor, double textSize, Color backgroundColor, string text, YAnchorPosition yAnchor = null, 
            int? yShift = null)
        {
            FSharpOption<double> doubleDef = 1d.ToFSharp();
            Annotation annotation = Annotation.init(
                X: x.ToFSharp(),
                Y: y.ToFSharp(),
                Align: AnnotationAlignment.Center,
                ArrowColor: null,
                ArrowHead: ArrowHead.Square,
                ArrowSide: ArrowSide.None,
                ArrowSize: null,
                AX: doubleDef,
                AXRef: doubleDef,
                AY: doubleDef,
                AYRef: doubleDef,
                BGColor: backgroundColor,
                BorderColor: null,
                BorderPad: null,
                BorderWidth: null,
                CaptureEvents: null,
                ClickToShow: null,
                Font: Font.init(Size: textSize, Color: textColor),
                Height: null,
                HoverLabel: null,
                HoverText: null,
                Name: text,
                Opacity: null,
                ShowArrow: null,
                StandOff: null,
                StartArrowHead: null,
                StartArrowSize: null,
                StartStandOff: null,
                TemplateItemName: null,
                Text: text,
                TextAngle: null,
                VAlign: VerticalAlign.Middle,
                Visible: null,
                Width: null,
                XAnchor: XAnchorPosition.Center,
                XClick: doubleDef,
                XRef: doubleDef,
                XShift: null,
                YAnchor: yAnchor ?? YAnchorPosition.Middle,
                YClick: doubleDef,
                YRef: doubleDef,
                YShift: yShift?.ToFSharp());
            return annotation;
        }

        public static GenericChart GetCandlestickChart(
            double[] o,
            double[] h,
            double[] l,
            double[] c,
            DateTime[] d,
            string name,
            Rangebreak[] rangeBreaks = null)
        {
            GenericChart candlestickChart = Chart2D.Chart.Candlestick
                <double, double, double, double, DateTime, string>(
                    o,
                    h,
                    l,
                    c,
                    X: d,
                    IncreasingColor: LONG_COLOR.ToFSharp(),
                    DecreasingColor: SHORT_COLOR.ToFSharp(),
                    Name: name,
                    ShowLegend: false);

            GenericChart resultChart = Chart.Combine(
                    Array.Empty<GenericChart>().Concat(new[] { candlestickChart }))
                .WithXAxisRangeSlider(RangeSlider.init(Visible: false))
                .WithConfig(Config.init(
                    StaticPlot: true,
                    Responsive: false))
                .WithLayout(Layout.init<string>(
                    PlotBGColor: BLACK_COLOR,
                    PaperBGColor: BLACK_COLOR,
                    Font: Font.init(Color: WHITE_COLOR)))
                .WithLayoutGrid(LayoutGrid.init(
                    Rows: 0,
                    Columns: 0,
                    XGap: 0d,
                    YGap: 0d))
                .WithXAxis(LinearAxis
                    .init<DateTime, DateTime, DateTime, DateTime, DateTime, DateTime, DateTime, DateTime>(
                        Rangebreaks: new FSharpOption<IEnumerable<Rangebreak>>(rangeBreaks),
                        GridColor: SEMI_WHITE_COLOR,
                        ShowGrid: true))
                .WithYAxis(LinearAxis
                    .init<DateTime, DateTime, DateTime, DateTime, DateTime, DateTime, DateTime, DateTime>(
                        GridColor: SEMI_WHITE_COLOR,
                        ShowGrid: true))
                .WithYAxisStyle(Side: Side.Right, title: null);

            return resultChart;
        }

        private static string GetTempString =>
            Path.GetFileNameWithoutExtension(Path.GetTempFileName());

        private static string GetChartFileName(ModelPattern model)
        {
            string name = model.Model.ToString().ToLowerInvariant();
            string fileName = $"{name}_{model.Candles.Count}_{GetTempString}";
            return fileName;
        }

        public static void SaveResultFiles(
            ModelPattern model, string folderToSave, byte filterLevel = 0)
        {
            string fileName = GetChartFileName(model);
            SaveChart(model, folderToSave, filterLevel);

            string jsonFilePath = Path.Join(folderToSave, $"{fileName}.json");
            string json = JsonConvert.SerializeObject(
                model.ToJson(), Formatting.Indented);
            File.WriteAllText(jsonFilePath, json);
        }

        public static void SaveChart(
            ModelPattern model, string folderToSave, byte filterLevel = 0)
        {
            string name = model.Model.ToString().Replace("_", " ").ToUpperInvariant();
            List<JsonCandleExport> candles = model.Candles;
            GenericChart chart = GetCandlestickChart(candles, name);

            const byte chartFontSizeCorrect = (byte)CHART_FONT_MAIN - 4;
            if (model.PatternKeyPoints != null)
            {
                var annotations = new List<Annotation>();
                bool isUp = model.Candles[0].O - model.Candles[^1].C > 0;

                foreach (DateTime patternKeyDt in model.PatternKeyPoints.Keys)
                {
                    IEnumerable<PatternKeyPoint> points =
                        model.PatternKeyPoints[patternKeyDt]
                            .Where(a => a.Notation.Level >= filterLevel);
                    IOrderedEnumerable<PatternKeyPoint> ordered =
                        isUp
                            ? points.OrderByDescending(a => a.Notation.Level)
                            : points.OrderBy(a => a.Notation.Level);

                    int offset = chartFontSizeCorrect;
                    foreach (PatternKeyPoint point in ordered)
                    {
                        int size = chartFontSizeCorrect + point.Notation.FontSize;
                        annotations.Add(GetAnnotation(
                            patternKeyDt,
                            point.Value,
                            WHITE_COLOR,
                            size,
                            SEMI_WHITE_COLOR,
                            point.Notation.NotationKey, null, offset));
                        offset += Convert.ToInt32(2 * size);
                    }

                    isUp = !isUp;
                }

                chart.WithAnnotations(annotations);
                chart.WithTitle(name);
            }

            string fileName = GetChartFileName(model);
            string pngPath = Path.Combine(folderToSave, fileName);
            chart.SavePNG(pngPath, null, 5000, 1000);
        }
    }
}
