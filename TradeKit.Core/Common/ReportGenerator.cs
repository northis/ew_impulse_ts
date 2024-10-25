using System.Text;
using Microsoft.FSharp.Core;
using Newtonsoft.Json;
using Plotly.NET;
using Plotly.NET.ImageExport;
using Plotly.NET.LayoutObjects;
using TradeKit.Core.Json;
using TradeKit.Core.PatternGeneration;
using TradeKit.Core.Resources;
using static Plotly.NET.StyleParam;

namespace TradeKit.Core.Common
{
    public static class ReportGenerator
    {
        private const string BACKGROUND_COLOR_KEY = "{BACKGROUND_COLOR}";
        private const string SEPARATOR_COLOR_KEY = "{SEPARATOR_COLOR}";
        private const string BAR_COLOR_KEY = "{BAR_COLOR}";
        private const string SYMBOL_COLOR_KEY = "{SYMBOL_COLOR}";
        private const string TRADE_COLOR_KEY = "{TRADE_COLOR}";
        private const string PRICE_RESULT_COLOR_KEY = "{PRICE_RESULT_COLOR}";
        private const string LABEL_COLOR_KEY = "{LABEL_COLOR}";
        private const string SL_COLOR_KEY = "{SL_COLOR}";
        private const string TP_COLOR_KEY = "{TP_COLOR}";
        private const string VALUE_COLOR_KEY = "{VALUE_COLOR}";

        private const string SYMBOL_KEY = "{SYMBOL}";
        private const string TRADE_TYPE_KEY = "{TRADE_TYPE}";
        private const string LOT_VOLUME_KEY = "{LOT_VOLUME}";
        private const string ENTRY_PRICE_KEY = "{ENTRY_PRICE}";
        private const string CURRENT_PRICE_KEY = "{CURRENT_PRICE}";
        private const string CLOSE_DATETIME_REASON_KEY = "{CLOSE_DATETIME_REASON}";
        private const string SL_PRICE_KEY = "{SL_PRICE}";
        private const string TP_PRICE_KEY = "{TP_PRICE}";
        private const string ORDER_ID_KEY = "{ORDER_ID}";
        private const string SWAP_KEY = "{SWAP}";
        private const string TAXES_KEY = "{TAXES}";
        private const string CHARGES_KEY = "{CHARGES}";
        private const string ENTRY_DATETIME_KEY = "{ENTRY_DATETIME}";
        private const string GROSS_PROFIT_KEY = "{GROSS_PROFIT}";


        private const string TRANSPARENT_COLOR_VALUE = "0,0,0,0";

        private const string BACKGROUND_COLOR_LIGHT_VALUE = "255, 255, 255";
        private const string BACKGROUND_COLOR_DARK_VALUE = "0, 0, 0";

        private const string SEPARATOR_COLOR_LIGHT_VALUE = "194, 194, 194";
        private const string SEPARATOR_COLOR_DARK_VALUE = "56, 56, 56";

        private const string TP_BAR_COLOR_LIGHT_VALUE = "1, 187, 44";
        private const string TP_BAR_COLOR_DARK_VALUE = "1, 187, 44";

        private const string SL_BAR_COLOR_LIGHT_VALUE = "220, 58, 46";
        private const string SL_BAR_COLOR_DARK_VALUE = "220, 58, 46";

        private const string SYMBOL_COLOR_LIGHT_VALUE = "0, 0, 0";
        private const string SYMBOL_COLOR_DARK_VALUE = "255, 255, 255";

        private const string SELL_COLOR_LIGHT_VALUE = "234, 74, 50";
        private const string SELL_COLOR_DARK_VALUE = "234, 74, 50";

        private const string BUY_COLOR_LIGHT_VALUE = "0, 122, 255";
        private const string BUY_COLOR_DARK_VALUE = "0, 122, 255";

        private const string PRICE_RESULT_COLOR_LIGHT_VALUE = "74, 74, 74";
        private const string PRICE_RESULT_COLOR_DARK_VALUE = "185, 185, 185";

        private const string LABEL_COLOR_LIGHT_VALUE = "144, 144, 144";
        private const string LABEL_COLOR_DARK_VALUE = "104, 104, 104";

        private const string SL_COLOR_LIGHT_VALUE = "189, 71, 68";
        private const string SL_COLOR_DARK_VALUE = "189, 71, 68";

        private const string TP_COLOR_LIGHT_VALUE = "32, 170, 58";
        private const string TP_COLOR_DARK_VALUE = "32, 170, 58";

        private const string VALUE_COLOR_LIGHT_VALUE = "80, 80, 80";
        private const string VALUE_COLOR_DARK_VALUE = "193, 193, 193";

        static ReportGenerator()
        {
            m_TradeResultTemplate = Encoding.Unicode.GetString(ResHolder.tradeResultTemplate);
        }

        public static string GetPngReport(IPosition position, string folderToSave)
        {

            //string fileName = GetChartFileName(model);
            //string pngPath = Path.Combine(folderToSave, fileName);
            //chart.SavePNG(pngPath, null, 5000, 1000);
        }

        private static readonly string m_TradeResultTemplate;

        public static readonly Color SHORT_COLOR = Color.fromHex("#EF5350");


        private static string GetTempString =>
            Path.GetFileNameWithoutExtension(Path.GetTempFileName());

        private static string GetChartFileName(ModelPattern model)
        {
            string name = model.Model.ToString().ToLowerInvariant();
            string fileName = $"{name}_{model.Candles.Count}_{GetTempString}";
            return fileName;
        }


    }
}
