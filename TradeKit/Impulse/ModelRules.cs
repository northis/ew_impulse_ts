using System;
using System.Collections.Generic;
using TradeKit.Core;

namespace TradeKit.Impulse
{
    internal record ModelRules(ElliottModelType[][] Models,
        Func<List<Candle>, ElliottModelResult> GetElliottModelResult);
}
