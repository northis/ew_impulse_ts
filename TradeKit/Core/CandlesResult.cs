using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TradeKit.Core
{
    public record CandlesResult(CandlePatternType Type, bool IsBull, int Index);
}
