using System;
using System.Collections.Generic;
using TradeKit.Core;

namespace TradeKit.Impulse
{
    public record ModelRules(Dictionary<string, ElliottModelType[]> Models,
        Func<List<BarPoint>, ElliottModelResult> GetElliottModelResult);
}
