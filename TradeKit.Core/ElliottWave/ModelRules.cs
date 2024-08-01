namespace TradeKit.Core.ElliottWave
{
    public record ModelRules(Dictionary<string, ElliottModelType[]> Models, double ProbabilityCoefficient = 1);
}
