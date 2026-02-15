using Microsoft.FSharp.Core;
using PuppeteerSharp.Input;
using System.Globalization;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.Gartley;
using TradeKit.Core.PriceAction;

namespace TradeKit.Core.Common
{
    public static class Extensions
    {
        private static readonly TimeSpan TIME_OFFSET = DateTime.UtcNow - DateTime.Now;

        /// <summary>
        /// To SVG path point.
        /// </summary>
        public static string ToSvgPoint(this BarPoint item)
        {
            string xVal = item.Value.ToString(CultureInfo.InvariantCulture);
            string xDat = item.OpenTime.Add(TIME_OFFSET).ToUnixMilliseconds().ToString(CultureInfo.InvariantCulture);
            return $"{xDat} {xVal}";
        }

        /// <summary>
        /// DateTime to UNIX milliseconds.
        /// </summary>
        public static double ToUnixMilliseconds(this DateTime item)
        {
            return (item - DateTime.UnixEpoch).TotalMilliseconds;
        }

        /// <summary>
        /// Gets percent view.
        /// </summary>
        public static int ToPercent(this double item)
        {
            return Convert.ToInt32(item * 100);
        }

#if !GARTLEY_PROD
        /// <summary>
        /// Converts to F# object.
        /// </summary>
        public static FSharpOption<T> ToFSharp<T>(this T item)
        {
            return new FSharpOption<T>(item);
        }
#endif


#if !GARTLEY_PROD
        private static readonly Dictionary<CandlePatternType, string> CANDLE_PATTERN_NAME_MAP = new()
        {
            {CandlePatternType.DOWN_PIN_BAR, "PB\n ↓"},
            {CandlePatternType.UP_PIN_BAR, " ↑\nPB"},
            {CandlePatternType.DOWN_PIN_BAR_TRIO, "PBT\n  ↓"},
            {CandlePatternType.UP_PIN_BAR_TRIO, "  ↑\nPBT"},
            {CandlePatternType.DOWN_OUTER_BAR, "OB\n ↓"},
            {CandlePatternType.UP_OUTER_BAR, " ↑\nOB"},
            {CandlePatternType.DOWN_INNER_BAR, "IB\n ↓"},
            {CandlePatternType.UP_INNER_BAR, " ↑\nIB"},
            {CandlePatternType.DOWN_OUTER_BAR_BODIES, "OBB\n  ↓"},
            {CandlePatternType.UP_OUTER_BAR_BODIES, "  ↑\nOBB"},
            {CandlePatternType.DOWN_PPR, "PPR\n  ↓"},
            {CandlePatternType.UP_PPR, "  ↑\nPPR"},
            {CandlePatternType.INVERTED_HAMMER, "IH\n ↓"},
            {CandlePatternType.HAMMER, "↑\nH"},
            {CandlePatternType.UP_RAILS, "↑\nR"},
            {CandlePatternType.DOWN_RAILS, "R\n↓"},
            {CandlePatternType.UP_PPR_IB, "  ↑\nP+I"},
            {CandlePatternType.DOWN_PPR_IB, "P+I\n  ↓"},
            {CandlePatternType.UP_DOUBLE_INNER_BAR, "  ↑\nDIB"},
            {CandlePatternType.DOWN_DOUBLE_INNER_BAR, "DIB\n  ↓"},
            {CandlePatternType.UP_CPPR, "     ↑\nCPPR"},
            {CandlePatternType.DOWN_CPPR, "CPPR\n     ↓"},
            {CandlePatternType.DOWN_DOJI, "Doji\n  ↓"},
            {CandlePatternType.UP_DOJI, "  ↑\nDoji"},
            {CandlePatternType.DARK_CLOUD, "DC\n  ↓"},
            {CandlePatternType.PIECING_LINE, "  ↑\nPL"},
            {CandlePatternType.DOWN_HARAMI, "HAR\n   ↓"},
            {CandlePatternType.UP_HARAMI, "   ↑\nHAR"}
        };

        /// <summary>
        /// Formats the <see cref="CandlePatternType"/> enum.
        /// </summary>
        /// <param name="type">The candle pattern type.</param>
        public static string Format(this CandlePatternType type)
        {
            if (CANDLE_PATTERN_NAME_MAP.TryGetValue(type, out string val))
                return val;
            return type.ToString();
        } 
        
        /// <summary>
        /// Gets the readable name from the <see cref="CandlePatternType"/> enum.
        /// </summary>
        /// <param name="type">The candle pattern type.</param>
        public static string GetName(this CandlePatternType type)
        {
            return type.ToString().Replace("UP_","").Replace("DOWN_", "").Replace("_", " ").ToLower();
        }
#endif
        private static readonly Dictionary<ElliottModelType, string> ELLIOTT_MODEL_NAME_MAP = new()
        {
            {ElliottModelType.IMPULSE, "Impulse"},
            {ElliottModelType.SIMPLE_IMPULSE, "Simple Imp."},
            {ElliottModelType.DIAGONAL_CONTRACTING_INITIAL, "Diag. Contr. Init."},
            {ElliottModelType.DIAGONAL_CONTRACTING_ENDING, "Diag. Contr. End."},
            {ElliottModelType.DIAGONAL_EXPANDING_INITIAL, "Diag. Exp. Init."},
            {ElliottModelType.DIAGONAL_EXPANDING_ENDING, "Diag. Exp. End."},
            {ElliottModelType.TRIANGLE_CONTRACTING, "Tri. Contr."},
            {ElliottModelType.TRIANGLE_EXPANDING, "Tri. Exp."},
            {ElliottModelType.TRIANGLE_RUNNING, "Tri. Running"},
            {ElliottModelType.ZIGZAG, "Zigzag"},
            {ElliottModelType.DOUBLE_ZIGZAG, "Double ZZ"},
            {ElliottModelType.TRIPLE_ZIGZAG, "Triple ZZ"},
            {ElliottModelType.FLAT_REGULAR, "Flat Reg."},
            {ElliottModelType.FLAT_EXTENDED, "Flat Ext."},
            {ElliottModelType.FLAT_RUNNING, "Flat Running"},
            {ElliottModelType.COMBINATION, "Combination"},
        };

        /// <summary>
        /// Formats the <see cref="ElliottModelType"/> enum to a short display name.
        /// </summary>
        /// <param name="type">The Elliott model type.</param>
        public static string Format(this ElliottModelType type)
        {
            if (ELLIOTT_MODEL_NAME_MAP.TryGetValue(type, out string val))
                return val;
            return type.ToString();
        }

        private static readonly Dictionary<GartleyPatternType, string> GARTLEY_PATTERN_NAME_MAP = new()
        {
            {GartleyPatternType.ALT_BAT, "Alt. Bat"},
            {GartleyPatternType.BAT, "Bat"},
            {GartleyPatternType.BUTTERFLY, "Butterfly"},
            {GartleyPatternType.CRAB, "Crab"},
            {GartleyPatternType.CYPHER, "Cypher"},
            {GartleyPatternType.DEEP_CRAB, "Deep Crab"},
            {GartleyPatternType.GARTLEY, "Gartley"},
            {GartleyPatternType.SHARK, "Shark"},
            {GartleyPatternType.FIVE_ZERO, "5-0"},
            {GartleyPatternType.NEN_STAR, "Nen Star"},
            {GartleyPatternType.LEONARDO, "Leonardo"}
        };

        /// <summary>
        /// Formats the <see cref="GartleyPatternType"/> enum.
        /// </summary>
        /// <param name="type">The Gartley pattern type.</param>
        public static string Format(this GartleyPatternType type)
        {
            if (GARTLEY_PATTERN_NAME_MAP.TryGetValue(type, out string val))
                return val;
            return type.ToString();
        }

        /// <summary>
        /// Formats the <see cref="GartleyPatternType"/> enum.
        /// </summary>
        /// <param name="type">The Gartley pattern type.</param>
        public static string[] GetPointNames(this GartleyPatternType type)
        {
            return type switch
            {
                GartleyPatternType.SHARK => new[] { "0", "X", "A", "B", "C" },
                GartleyPatternType.FIVE_ZERO => new[]
                {
                    "0", "X", "A", "B", "C", "D"
                },
                _ => new[] { "X", "A", "B", "C", "D" }
            };
        }

        /// <summary>
        /// Gets the string for the specified value for ratios from 0 to 9.
        /// </summary>
        /// <param name="val">The double value.</param>
        public static string Ratio(this double val)
        {
            return val.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Adds the value to the sorted dict with list-backed value.
        /// </summary>
        /// <typeparam name="TK">The type of the key.</typeparam>
        /// <typeparam name="TV">The type of the value.</typeparam>
        /// <param name="sortedDictionary">The sorted dict.</param>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        public static void AddValue<TK, TV>(
            this SortedList<TK, List<TV>> sortedDictionary, TK key, TV value)
        {
            if (!sortedDictionary.TryGetValue(key, out List<TV> valList))
            {
                valList = new List<TV>();
                sortedDictionary[key] = valList;
            }

            valList.Add(value);
        }

        /// <summary>
        /// Slices the ordered array by value.
        /// </summary>
        /// <param name="inDoubles">The array of doubles.</param>
        /// <param name="startValue">The start value.</param>
        /// <param name="endValue">The end value.</param>
        /// <returns>The sliced array</returns>
        public static double[] RangeVal(
            this double[] inDoubles, double startValue, double endValue)
        {
            return inDoubles
                .SkipWhile(a => a < startValue)
                .TakeWhile(a => a <= endValue)
                .ToArray();
        }

        /// <summary>
        /// Removes according to the enumerable.
        /// </summary>
        /// <typeparam name="TK">The type of the key.</typeparam>
        /// <typeparam name="TV">The type of the value.</typeparam>
        /// <param name="sortedList">The sorted list.</param>
        /// <param name="toDeleteEnumerable">The enumerable to delete.</param>
        /// <returns>Removed items count.</returns>
        public static int RemoveWhere<TK, TV>(
            this IDictionary<TK, TV> sortedList, IEnumerable<TK> toDeleteEnumerable)
        {
            var keysToRemove = new List<TK>();
            foreach (TK key in toDeleteEnumerable)
            {
                keysToRemove.Add(key);
            }

            foreach (TK key in keysToRemove)
            {
                sortedList.Remove(key);
            }

            return keysToRemove.Count;
        }

        /// <summary>
        /// Removes, according to the func (left part of the dictionary).
        /// </summary>
        /// <typeparam name="TK">The type of the key.</typeparam>
        /// <typeparam name="TV">The type of the value.</typeparam>
        /// <param name="sortedList">The sorted list.</param>
        /// <param name="compareFunc">The function for comparing.</param>
        /// <returns>Removed items count.</returns>
        public static int RemoveLeft<TK, TV>(
            this SortedList<TK, TV> sortedList, Func<TK, bool> compareFunc)
        {
            return sortedList.RemoveWhere(sortedList.Keys.TakeWhile(compareFunc));
        }

        /// <summary>
        /// Removes the specified number of elements from the beginning of the <see cref="SortedList{TK, TV}"/>.
        /// </summary>
        /// <param name="sortedList">The sorted dictionary to remove elements from.</param>
        /// <param name="countToRemove">The number of elements to remove from the beginning of the dictionary.</param>
        /// <typeparam name="TK">The type of the keys in the dictionary.</typeparam>
        /// <typeparam name="TV">The type of the values in the dictionary.</typeparam>
        /// <returns>The number of elements removed from the dictionary.</returns>
        public static int RemoveLeftTop<TK, TV>(
            this SortedList<TK, TV> sortedList, int countToRemove)
        {
            List<TK> keysToDelete = sortedList.Keys.Take(countToRemove).ToList();
            int count = 0;
            foreach (TK key in keysToDelete)
            {
                sortedList.Remove(key);
                count++;
            }

            return count;
        }

        /// <summary>
        /// Removes, according to the func (right part of the dictionary).
        /// </summary>
        /// <typeparam name="TK">The type of the key.</typeparam>
        /// <typeparam name="TV">The type of the value.</typeparam>
        /// <param name="sortedList">The sorted list.</param>
        /// <param name="compareFunc">The function for comparing.</param>
        /// <returns>Removed items count.</returns>
        public static int RemoveRight<TK, TV>(
            this SortedDictionary<TK, TV> sortedList, Func<TK, bool> compareFunc)
        {
            return sortedList.RemoveWhere(sortedList.Keys.SkipWhile(compareFunc));
        }

        /// <summary>
        /// Gets a new <see cref="BarPoint"/> with given index.
        /// </summary>
        /// <param name="bp">The bar point.</param>
        /// <param name="index">The new bar index.</param>
        /// <param name="provider">Bar provider to update the open date.</param>
        public static BarPoint WithIndex(this BarPoint bp, int index, IBarsProvider provider)
        {
            return new BarPoint(bp.Value, provider.GetOpenTime(index), bp.BarTimeFrame, index);
        }

        /// <summary>
        /// Gets a new <see cref="BarPoint"/> with given price.
        /// </summary>
        /// <param name="bp">The bar point.</param>
        /// <param name="price">The changed price.</param>
        public static BarPoint WithPrice(this BarPoint bp, double price)
        {
            return new BarPoint(price, bp.OpenTime, bp.BarTimeFrame, bp.BarIndex);
        }

        /// <summary>
        /// Normalizes the extrema from Zigzag indicator.
        /// </summary>
        /// <param name="extrema">The extrema.</param>
        /// <param name="start">The start extremum.</param>
        /// <param name="end">The end extremum.</param>
        public static void NormalizeExtrema(
            this List<BarPoint> extrema, BarPoint start, BarPoint end)
        {
            if (extrema.Count == 0)
            {
                extrema.Add(start);
                extrema.Add(end);
            }
            else
            {
                if (extrema[0].OpenTime == start.OpenTime)
                {
                    extrema[0] = start;
                }
                else
                {
                    extrema.Insert(0, start);
                }

                if (extrema[^1].OpenTime == end.OpenTime)
                {
                    extrema[^1] = end;
                }
                else
                {
                    extrema.Add(end);
                }
            }

            const int minPointCount = 4;
            if (extrema.Count < minPointCount)
            {
                return;
            }

            // We want to leave only true extrema
            BarPoint current = start;
            bool direction = start > end;
            List<BarPoint> toDelete = null;
            for (int i = 1; i < extrema.Count; i++)
            {
                BarPoint extremum = extrema[i];
                bool newDirection = current < extremum;
                if (direction == newDirection)
                {
                    toDelete ??= new List<BarPoint>();
                    toDelete.Add(current);
                }

                direction = newDirection;
                current = extremum;
            }

            if (toDelete == null)
            {
                return;
            }

            foreach (BarPoint toDeleteItem in toDelete)
            {
                extrema.Remove(toDeleteItem);
            }
        }

        /// <summary>
        /// Gets the extremum between two dates.
        /// The start and end candles ARE NOT included.
        /// </summary>
        /// <param name="barProvider">The bar provider.</param>
        /// <param name="dateTimeStart">The start date time.</param>
        /// <param name="dateTimeEnd">The end date time.</param>
        /// <param name="isHigh">True if we want to find a high, false - if a low.</param>
        public static BarPoint GetExtremumBetween(
            this IBarsProvider barProvider,
            DateTime dateTimeStart,
            DateTime dateTimeEnd,
            bool isHigh)
        {
            int startIndex = barProvider.GetIndexByTime(dateTimeStart) + 1;//! +1
            int endIndex = barProvider.GetIndexByTime(dateTimeEnd);

            double currentValue = isHigh
                ? barProvider.GetHighPrice(startIndex)
                : barProvider.GetLowPrice(startIndex);
            int currentIndex = startIndex;

            for (int i = startIndex; i < endIndex; i++)//! <
            {
                if (isHigh && barProvider.GetHighPrice(i) >= currentValue ||
                    !isHigh && barProvider.GetLowPrice(i) <= currentValue)
                {
                    currentValue = isHigh
                        ? barProvider.GetHighPrice(i)
                        : barProvider.GetLowPrice(i);
                    currentIndex = i;
                }
            }

            return endIndex <= currentIndex ? null : new BarPoint(currentValue, currentIndex, barProvider);
        }

        /// <summary>
        /// Gets the high price.
        /// </summary>
        /// <param name="barProvider">The bar provider.</param>
        /// <param name="dateTime">The date time.</param>
        public static double GetHighPrice(
            this IBarsProvider barProvider, DateTime dateTime)
        {
            int index = barProvider.GetIndexByTime(dateTime);
            double res = barProvider.GetHighPrice(index);

            return res;
        }

        /// <summary>
        /// Gets the low price.
        /// </summary>
        /// <param name="barProvider">The bar provider.</param>
        /// <param name="dateTime">The date time.</param>
        public static double GetLowPrice(
            this IBarsProvider barProvider, DateTime dateTime)
        {
            int index = barProvider.GetIndexByTime(dateTime);
            double res = barProvider.GetLowPrice(index);

            return res;
        }

        /// <summary>
        /// Gets the close price.
        /// </summary>
        /// <param name="barProvider">The bar provider.</param>
        /// <param name="dateTime">The date time.</param>
        public static double GetClosePrice(
            this IBarsProvider barProvider, DateTime dateTime)
        {
            int index = barProvider.GetIndexByTime(dateTime);
            double res = barProvider.GetClosePrice(index);

            return res;
        }

        /// <summary>
        /// Initializes the IsHighFirst property. This can be costly, we do this on-demand only.
        /// </summary>
        /// <param name="candle">The candle we should check</param>
        /// <param name="barsFunc">The time frame to bars provider function.</param>
        /// <param name="candleTimeFrame">The TF of the candle.</param>
        public static void InitIsHighFirst(
            this Candle candle, Func<ITimeFrame, IBarsProvider> barsFunc, ITimeFrame candleTimeFrame)
        {
            if (candle.IsHighFirst.HasValue || !candle.Index.HasValue)
                return;

            IBarsProvider currentBarsProvider = barsFunc(candleTimeFrame);

            DateTime startDate = currentBarsProvider.GetOpenTime(candle.Index.Value);
            TimeFrameInfo timeFrameInfo = TimeFrameHelper.GetTimeFrameInfo(candleTimeFrame);
            DateTime endDate = startDate + timeFrameInfo.TimeSpan;

            ITimeFrame currentTimeFrame = candleTimeFrame;

            for (;;)
            {
                var prevTimeFrame = TimeFrameHelper.GetPreviousTimeFrameInfo(currentTimeFrame).TimeFrame;
                if (currentTimeFrame == prevTimeFrame)
                    break;

                IBarsProvider barsProvider1M = barsFunc(prevTimeFrame);
                int startIndex1M = barsProvider1M.GetIndexByTime(startDate);
                if (startIndex1M == -1)
                    barsProvider1M.LoadBars(startDate);

                if (startIndex1M == -1)
                    break;

                int endIndex1M = barsProvider1M.GetIndexByTime(endDate);

                var highIndex = 0;
                var lowIndex = 0;

                for (int i = startIndex1M; i <= endIndex1M; i++)
                {
                    double high = barsProvider1M.GetHighPrice(i);
                    if (Math.Abs(candle.H - high) < double.Epsilon)
                    {
                        highIndex++;
                    }

                    double low = barsProvider1M.GetLowPrice(i);
                    if (Math.Abs(candle.L - low) < double.Epsilon)
                    {
                        lowIndex++;
                    }

                    if (highIndex == lowIndex)
                        continue;

                    break;
                }

                candle.IsHighFirst = highIndex > lowIndex;
                break;
            }
        }

        /// <summary>
        /// Returns the amount of volume based on your provided risk percentage and stop loss
        /// </summary>
        /// <param name="symbol">The symbol</param>
        /// <param name="riskPercentage">Risk percentage amount</param>
        /// <param name="accountBalance">The account balance</param>
        /// <param name="rangeInPips">Setup range in Pips</param>
        public static double GetVolume(this ISymbol symbol, double riskPercentage, double accountBalance, double rangeInPips)
        {
            return riskPercentage / (Math.Abs(rangeInPips) * symbol.PipValue / accountBalance * 100);
        }

        /// <summary>
        /// Saves OHLC candles to a .csv file.
        /// </summary>
        /// <param name="provider">The provider.</param>
        /// <param name="start">The start.</param>
        /// <param name="end">The end.</param>
        /// <param name="pathToSave">The path to save.</param>
        public static void SaveCandles(this IBarsProvider provider, DateTime start, DateTime end, string pathToSave)
        {
            // Ensure the directory exists
            string directory = Path.GetDirectoryName(pathToSave);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var writer = new StreamWriter(pathToSave);
            writer.WriteLine($"Time{Helper.CSV_SEPARATOR}Open{Helper.CSV_SEPARATOR}High{Helper.CSV_SEPARATOR}Low{Helper.CSV_SEPARATOR}Close");
        
            int startIndex = provider.GetIndexByTime(start);
            int endIndex = provider.GetIndexByTime(end);
                
            // Get the number of decimal places from the symbol
            int digits = provider.BarSymbol.Digits;
            string formatSpecifier = $"F{digits}";
        
            for (int i = startIndex; i <= endIndex; i++)
            {
                DateTime openTime = provider.GetOpenTime(i);
                double open = provider.GetOpenPrice(i);
                double high = provider.GetHighPrice(i);
                double low = provider.GetLowPrice(i);
                double close = provider.GetClosePrice(i);
                    
                string formattedLine = string.Format(CultureInfo.InvariantCulture,
                    "{0:"+Helper.DATE_COLLECTION_FORMAT+"}{5}{1:" + formatSpecifier + "}{5}{2:" + formatSpecifier + "}{5}{3:" + formatSpecifier + "}{5}{4:" + formatSpecifier + "}",
                    openTime, open, high, low, close, Helper.CSV_SEPARATOR);

                writer.WriteLine(formattedLine);
            }
        }
        
        /// <summary>
        /// Saves OHLC candles to a .csv file based on a date range string in the format "start->end".
        /// </summary>
        /// <param name="provider">The provider.</param>
        /// <param name="dateRangeString">The date range string in the format "yyyy-MM-ddTHH:mm:ss->yyyy-MM-ddTHH:mm:ss".</param>
        /// <returns>Path to the saved file or null if not saved.</returns>
        public static string SaveCandlesForDateRange(this IBarsProvider provider, string dateRangeString)
        {
            if (string.IsNullOrEmpty(dateRangeString))
                return null;
                
            // Parse the date range
            string[] dateParts = dateRangeString.Split(new[] { Helper.DATE_COLLECTION_SEPARATOR }, StringSplitOptions.None);
            if (dateParts.Length != 2)
                return null;
                
            if (!DateTime.TryParse(dateParts[0], out DateTime startDate))
                return null;
                
            if (!DateTime.TryParse(dateParts[1], out DateTime endDate))
                return null;
                
            // Ensure the directory exists
            if (!Directory.Exists(Helper.DirectoryToSaveResults))
                Directory.CreateDirectory(Helper.DirectoryToSaveResults);
                
            string fileName = string.Format(
                Helper.CANDLE_FILE_NAME_FORMAT,
                provider.BarSymbol.Name,
                provider.TimeFrame.ShortName,
                startDate.ToString(Helper.DATE_COLLECTION_FORMAT).Replace(":","-"),
                endDate.ToString(Helper.DATE_COLLECTION_FORMAT).Replace(":","-"));
                
            string filePath = Path.Combine(Helper.DirectoryToSaveResults, fileName);
            
            // Save the candles
            provider.SaveCandles(startDate, endDate, filePath);
            
            return filePath;
        }

        /// <summary>
        /// Determines whether the spread of the specified symbol is considered large based on the take profit (tp)
        /// and stop loss (sl) range compared to a maximum spread ratio.
        /// </summary>
        /// <param name="manager">The trade view manager responsible for retrieving the spread value.</param>
        /// <param name="symbol">The trading symbol for which the spread is being evaluated.</param>
        /// <param name="tp">The take-profit value associated with a trade.</param>
        /// <param name="sl">The stop-loss value associated with a trade.</param>
        /// <returns>True if the spread is larger than the allowable range based on the maximum spread ratio; otherwise, false.</returns>
        public static bool IsBigSpread(this ITradeViewManager manager,
            ISymbol symbol,
            double tp, double sl)
        {
            double spread = manager.GetSpread(symbol);
            double rangeLen = Math.Abs(tp - sl);

            return spread > 0 && spread / rangeLen > Helper.MAX_SPREAD_RATIO;
        }
    }
}