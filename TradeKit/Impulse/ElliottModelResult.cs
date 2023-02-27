using TradeKit.Core;

namespace TradeKit.Impulse
{
    public record ElliottModelResult(
        ElliottModelType Type, BarPoint[] Extrema, ElliottModelResult[] ChildModels);
}
