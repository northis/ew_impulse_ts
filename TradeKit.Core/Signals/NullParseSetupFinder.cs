﻿using TradeKit.Core.Common;

namespace TradeKit.Core.Signals
{
    /// <summary>
    /// Do nothing setup finder
    /// </summary>
    public class NullParseSetupFinder : ParseSetupFinder
    {
        public NullParseSetupFinder(IBarsProvider mainBarsProvider, ISymbol symbol) : base(mainBarsProvider, symbol, string.Empty,true,true)
        {
        }

        public override void CheckBar(int index)
        {
        }
    }
}
