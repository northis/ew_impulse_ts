using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TradeKit.Core.Common;

namespace TradeKit.Core.Indicators
{
    /*
     *public class SupertrendIndicator : Indicator, Supertrend, StandardIndicator
         {
           private IndicatorDataSeries _upBuffer;
           private IndicatorDataSeries _downBuffer;
           private AverageTrueRange _averageTrueRange;
           private IndicatorDataSeries _trend;
       
           [Parameter(DefaultValue = 10, MinValue = 1, MaxValue = 2000)]
           public int Periods { get; set; }
       
           [Parameter(DefaultValue = 3.0)]
           public double Multiplier { get; set; }
       
           [cTrader.Automate.Indicators.Attributes.Shift]
           [Parameter(DefaultValue = 0, MinValue = 0, MaxValue = 200)]
           public int Shift { get; set; }
       
           [Output("Up Trend", Color = Colors.Green, PlotType = PlotType.Points, Thickness = 2f)]
           public IndicatorDataSeries UpTrend { get; set; }
       
           [Output("Down Trend", Color = Colors.Red, PlotType = PlotType.Points, Thickness = 2f)]
           public IndicatorDataSeries DownTrend { get; set; }
       
           protected override void Initialize()
           {
             this._trend = this.CreateDataSeries();
             this._upBuffer = this.CreateDataSeries();
             this._downBuffer = this.CreateDataSeries();
             this._averageTrueRange = this.Indicators.AverageTrueRange(this.Periods, MovingAverageType.Simple);
           }
       
           private void InitDataSeries(int index)
           {
             this.UpTrend[checked (index + this.Shift)] = double.NaN;
             this.DownTrend[checked (index + this.Shift)] = double.NaN;
           }
       
           private void CalculateSuperTrendLogic(int index, double median, double averageTrueRangeValue)
           {
             if (this.MarketSeries.Close[index] > this._upBuffer[checked (index - 1)])
               this._trend[index] = 1.0;
             else if (this.MarketSeries.Close[index] < this._downBuffer[checked (index - 1)])
             {
               this._trend[index] = -1.0;
             }
             else
             {
               IndicatorDataSeries trend = this._trend;
               int num1 = index;
               double num2 = this._trend[checked (index - 1)];
               double num3 = num2 == -1.0 ? -1.0 : (num2 != 1.0 ? this._trend[index] : 1.0);
               int index1 = num1;
               double num4 = num3;
               trend[index1] = num4;
             }
             IndicatorDataSeries upBuffer = this._upBuffer;
             int num5 = index;
             double num6;
             if (this._trend[index] < 0.0)
             {
               if (this._trend[checked (index - 1)] > 0.0)
               {
                 num6 = median + this.Multiplier * averageTrueRangeValue;
                 goto label_11;
               }
               else if (this._upBuffer[index] > this._upBuffer[checked (index - 1)])
               {
                 num6 = this._upBuffer[checked (index - 1)];
                 goto label_11;
               }
             }
             num6 = this._upBuffer[index];
       label_11:
             int index2 = num5;
             double num7 = num6;
             upBuffer[index2] = num7;
             IndicatorDataSeries downBuffer = this._downBuffer;
             int num8 = index;
             double num9;
             if (this._trend[index] > 0.0)
             {
               if (this._trend[checked (index - 1)] < 0.0)
               {
                 num9 = median - this.Multiplier * averageTrueRangeValue;
                 goto label_17;
               }
               else if (this._downBuffer[index] < this._downBuffer[checked (index - 1)])
               {
                 num9 = this._downBuffer[checked (index - 1)];
                 goto label_17;
               }
             }
             num9 = this._downBuffer[index];
       label_17:
             int index3 = num8;
             double num10 = num9;
             downBuffer[index3] = num10;
           }
       
           private void DrawIndicator(int index)
           {
             if (this._trend[index] == 1.0)
             {
               this.UpTrend[checked (index + this.Shift)] = this._downBuffer[index];
             }
             else
             {
               if (this._trend[index] != -1.0)
                 return;
               this.DownTrend[checked (index + this.Shift)] = this._upBuffer[index];
             }
           }
       
           public override void Calculate(int index)
           {
             this.InitDataSeries(index);
             double median = (this.MarketSeries.High[index] + this.MarketSeries.Low[index]) / 2.0;
             double averageTrueRangeValue = this._averageTrueRange.Result[index];
             this._upBuffer[index] = median + this.Multiplier * averageTrueRangeValue;
             this._downBuffer[index] = median - this.Multiplier * averageTrueRangeValue;
             if (index < 1)
             {
               this._trend[index] = 1.0;
             }
             else
             {
               this.CalculateSuperTrendLogic(index, median, averageTrueRangeValue);
               this.DrawIndicator(index);
             }
           }
         }
     *
     */
    
    public class SupertrendFinder : BaseFinder<double>
    {
        public SupertrendFinder(IBarsProvider barsProvider, bool useAutoCalculateEvent = true, int defaultCleanBarsCount = 500) : base(barsProvider, useAutoCalculateEvent, defaultCleanBarsCount)
        {
        }

        public override void OnCalculate(int index, DateTime openDateTime)
        {
            throw new NotImplementedException();
        }
    }
}
