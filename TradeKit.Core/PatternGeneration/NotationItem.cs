using TradeKit.Core.ElliottWave;

namespace TradeKit.Core.PatternGeneration
{
    public record NotationItem(
        ElliottModelType Type, byte Level, string Key, string NotationKey, byte FontSize);
}
