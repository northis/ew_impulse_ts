using TradeKit.Impulse;

namespace TradeKit.PatternGeneration
{
    public record NotationItem(
        ElliottModelType Type, byte Level, string Key, string NotationKey, byte FontSize);
}
