using TradeKit.Core;

namespace TradeKit.Impulse
{
    internal record ElliottModelResult(
        ElliottModelType Type, BarPoint[] Extrema, ElliottModelResult[] ChildModels);
}
