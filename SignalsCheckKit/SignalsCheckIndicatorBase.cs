using System.Diagnostics;
using cAlgo.API;

namespace SignalsCheckKit
{
    [Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
    public class SignalsCheckIndicatorBase : Indicator
    {
        [Parameter("SignalHistoryFilePath", DefaultValue = null)]
        public string SignalHistoryFilePath { get; set; }

        protected override void Initialize()
        {
            base.Initialize();
            Debugger.Launch();
            List<Signal> signals = SignalParser.ParseSignals(SymbolName, SignalHistoryFilePath);
        }

        public override void Calculate(int index)
        {
            //Chart.DrawRectangle(index.ToString(), index, Bars[index].Open,index+10,)
            //Bars[]
        }
    }
}
