using TradeKit.Core.Common;

namespace TradeKit.Core.PriceAction
{
    public record CandleParams(Candle[] Candles, ISymbol Symbol);
}
