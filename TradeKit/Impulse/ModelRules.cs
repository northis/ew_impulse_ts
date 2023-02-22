using System;
using System.Collections.Generic;
using TradeKit.Core;

namespace TradeKit.Impulse
{
    internal record ModelRules(Dictionary<string, ElliottModelType[]> Models,
        Func<List<Candle>, ElliottModelResult> GetElliottModelResult);
}
