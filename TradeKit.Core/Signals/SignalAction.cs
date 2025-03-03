namespace TradeKit.Core.Signals
{
    [Flags]
    public enum SignalAction
    {
        DEFAULT = 0,
        ENTER_BUY = 1,
        ENTER_SELL= 2,
        SET_TP = 4,
        SET_SL = 8,
        CLOSE = 16,
        SET_BREAKEVEN = 32,
        HIT_TP = 64,
        HIT_SL = 128,
        LIMIT = 256,
        ACTIVATED = 512
    }
}
