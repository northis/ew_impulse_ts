using System.Globalization;
using System.Text;
using SkiaSharp;
using Svg.Skia;
using TradeKit.Core.Resources;
using SKPicture = SkiaSharp.SKPicture;

namespace TradeKit.Core.Common
{
    /// <summary>
    /// Generates result reports for the trade setups.
    /// </summary>
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
        private const string PROFIT_COLOR_KEY = "{PROFIT_COLOR}";

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

        private const string LOSS_COLOR_LIGHT_VALUE = "234, 74, 50";
        private const string LOSS_COLOR_DARK_VALUE = "234, 74, 50";

        private const string PROFIT_COLOR_LIGHT_VALUE = "0, 122, 255";
        private const string PROFIT_COLOR_DARK_VALUE = "0, 122, 255";

        private const string PLACEHOLDER = "-";
        private const string TP_HIT_SUFFIX = ", [tp]";
        private const string SL_HIT_SUFFIX = ", [sl]";
        private const string BUY_VALUE = "buy";
        private const string SELL_VALUE = "sell";
        private const string DATE_FORMAT = "yyyy.MM.dd HH:mm:ss";

        private const int REPORT_WIDTH = 1170;
        private const int REPORT_HEIGHT = 470;
        private const int DEFAULT_ACCURACY_DIGITS = 2;

        static ReportGenerator()
        {
            TRADE_RESULT_TEMPLATE = Encoding.UTF8.GetString(ResHolder.tradeResultTemplate);
        }

        private static string GetSvg(IPosition position, PositionClosedState state, bool isLight)
        {
            bool isUp = position.Type == PositionType.BUY;

            var sb = new StringBuilder(TRADE_RESULT_TEMPLATE);
            sb.Replace(BACKGROUND_COLOR_KEY, isLight ? BACKGROUND_COLOR_LIGHT_VALUE : BACKGROUND_COLOR_DARK_VALUE);
            sb.Replace(SEPARATOR_COLOR_KEY, isLight ? SEPARATOR_COLOR_LIGHT_VALUE : SEPARATOR_COLOR_DARK_VALUE);

            sb.Replace(SYMBOL_KEY, position.Symbol.Name);
            sb.Replace(SYMBOL_COLOR_KEY, isLight ? SYMBOL_COLOR_LIGHT_VALUE : SYMBOL_COLOR_DARK_VALUE);

            sb.Replace(TRADE_TYPE_KEY, isUp ? BUY_VALUE : SELL_VALUE);
            sb.Replace(TRADE_COLOR_KEY,
                isUp
                    ? isLight ? BUY_COLOR_LIGHT_VALUE : BUY_COLOR_DARK_VALUE
                    : isLight
                        ? SELL_COLOR_LIGHT_VALUE
                        : SELL_COLOR_DARK_VALUE);

            sb.Replace(LOT_VOLUME_KEY, Helper.PriceFormat(position.Quantity, 2));
            sb.Replace(ENTRY_PRICE_KEY, Helper.PriceFormat(position.EntryPrice, position.Symbol.Digits));
            sb.Replace(CURRENT_PRICE_KEY, Helper.PriceFormat(position.CurrentPrice, position.Symbol.Digits));

            sb.Replace(PRICE_RESULT_COLOR_KEY, isLight
                ? PRICE_RESULT_COLOR_LIGHT_VALUE
                : PRICE_RESULT_COLOR_DARK_VALUE);

            string tpText = PLACEHOLDER;
            string slText = PLACEHOLDER;
            string reason = position.CloseDateTime.HasValue
                ? position.CloseDateTime.Value.ToString(DATE_FORMAT)
                : string.Empty;
            string barColor = TRANSPARENT_COLOR_VALUE;
            bool isTpHit = false;
            if (position.TakeProfit.HasValue)
            {
                isTpHit = state == PositionClosedState.TAKE_PROFIT;
                sb.Replace(TP_COLOR_KEY,
                    isTpHit
                        ? isLight ? TP_COLOR_LIGHT_VALUE : TP_COLOR_DARK_VALUE
                        : isLight
                            ? VALUE_COLOR_LIGHT_VALUE
                            : VALUE_COLOR_DARK_VALUE);

                tpText = Helper.PriceFormat(position.TakeProfit.Value, position.Symbol.Digits);
                if (isTpHit)
                {
                    barColor = isLight ? TP_BAR_COLOR_LIGHT_VALUE : TP_BAR_COLOR_DARK_VALUE;
                    reason += TP_HIT_SUFFIX;
                }
            }

            if (position.StopLoss.HasValue)
            {
                bool isSlHit = state == PositionClosedState.STOP_LOSS && !isTpHit;

                sb.Replace(SL_COLOR_KEY,
                    isSlHit
                        ? isLight ? SL_COLOR_LIGHT_VALUE : SL_COLOR_DARK_VALUE
                        : isLight
                            ? VALUE_COLOR_LIGHT_VALUE
                            : VALUE_COLOR_DARK_VALUE);

                slText = Helper.PriceFormat(position.StopLoss.Value, position.Symbol.Digits);
                if (isSlHit)
                {
                    barColor = isLight ? SL_BAR_COLOR_LIGHT_VALUE : SL_BAR_COLOR_DARK_VALUE;
                    reason += SL_HIT_SUFFIX;
                }
            }

            sb.Replace(BAR_COLOR_KEY, barColor);
            sb.Replace(CLOSE_DATETIME_REASON_KEY, reason);
            sb.Replace(TP_PRICE_KEY, tpText);
            sb.Replace(SL_PRICE_KEY, slText);
            sb.Replace(VALUE_COLOR_KEY, isLight ? VALUE_COLOR_LIGHT_VALUE : VALUE_COLOR_DARK_VALUE);

            sb.Replace(ORDER_ID_KEY, position.Id.ToString());
            sb.Replace(LABEL_COLOR_KEY, isLight ? LABEL_COLOR_LIGHT_VALUE : LABEL_COLOR_DARK_VALUE);

            sb.Replace(SWAP_KEY, Helper.PriceFormat(position.Swap, DEFAULT_ACCURACY_DIGITS));
            sb.Replace(TAXES_KEY, Helper.PriceFormat(0d, DEFAULT_ACCURACY_DIGITS));
            sb.Replace(CHARGES_KEY, Helper.PriceFormat(position.Charges, DEFAULT_ACCURACY_DIGITS));
            sb.Replace(GROSS_PROFIT_KEY, Helper.PriceFormat(position.GrossProfit, DEFAULT_ACCURACY_DIGITS));

            sb.Replace(PROFIT_COLOR_KEY,
                position.GrossProfit > 0
                    ? isLight ? PROFIT_COLOR_LIGHT_VALUE : PROFIT_COLOR_DARK_VALUE
                    : isLight
                        ? LOSS_COLOR_LIGHT_VALUE
                        : LOSS_COLOR_DARK_VALUE);

            sb.Replace(ENTRY_DATETIME_KEY, position.EnterDateTime.ToString(DATE_FORMAT));
            sb.Replace(CLOSE_DATETIME_REASON_KEY,
                position.CloseDateTime.HasValue
                    ? position.CloseDateTime.Value.ToString(DATE_FORMAT, CultureInfo.InvariantCulture)
                    : string.Empty);

            return sb.ToString();
        }

        /// <summary>
        /// Gets the PNG report from the position object given.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <param name="state">The reason of closing</param>
        /// <param name="folderToSave">The folder to save.</param>
        /// <param name="isLight">if set to <c>true</c> we want to use light theme for the report, otherwise - dark one.</param>
        /// <returns>Path to the generated .png file.</returns>
        public static string GetPngReport(IPosition position, PositionClosedState state, string folderToSave, bool isLight = true)
        {
            string svgBody = GetSvg(position, state, isLight);
            string pngPath = Path.Combine(folderToSave, $"{GetTempString}.png");
            using var svg = new SKSvg();
            SKPicture ss = svg.FromSvg(svgBody);
            if (ss == null)
                return null;

            svg.Save(pngPath, SKColors.Empty);
            return pngPath;
        }

        private static readonly string TRADE_RESULT_TEMPLATE;

        private static string GetTempString =>
            Path.GetFileNameWithoutExtension(Path.GetTempFileName());
    }
}
