namespace ImpulseFinder.Bot.OpenApi
{
    public record SymbolQuote(long Id, double Bid, double Ask);

    public record AccountInfo
    {
        public double Balance { get; init; }

        public double Equity { get; init; }

        public double MarginUsed { get; init; }

        public double FreeMargin { get; init; }

        public double MarginLevel { get; init; }

        public double UnrealizedGrossProfit { get; init; }

        public double UnrealizedNetProfit { get; init; }

        public string Currency { get; init; }

        public DateTimeOffset RegistrationTime { get; init; }

        //public static AccountInfo FromModel(AccountModel model) => new()
        //{
        //    Balance = model.Balance,
        //    Equity = model.Equity,
        //    FreeMargin = model.FreeMargin,
        //    MarginLevel = model.MarginLevel,
        //    MarginUsed = model.MarginUsed,
        //    UnrealizedGrossProfit = model.UnrealizedGrossProfit,
        //    UnrealizedNetProfit = model.UnrealizedNetProfit,
        //    Currency = model.Currency,
        //    RegistrationTime = model.RegistrationTime
        //};
    }

    public record Error(string Message, string Type);

    public record Ohlc(long T, double O, double H, double L, double C);
    public record SymbolData(string Name, IEnumerable<Ohlc> Ohlc);
}