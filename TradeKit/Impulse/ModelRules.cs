using System.Collections.Generic;

namespace TradeKit.Impulse
{
    public record ModelRules(Dictionary<string, ElliottModelType[]> Models, double ProbabilityCoefficient = 1);
}
