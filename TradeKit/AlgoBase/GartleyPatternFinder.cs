using System;
using System.Collections.Generic;
using TradeKit.Core;

namespace TradeKit.AlgoBase
{
    internal class GartleyPatternFinder
    {
        private readonly double m_ShadowAllowance;
        private readonly IBarsProvider m_BarsProvider;

        private static Dictionary<GartleyPatternType, GartleyPattern> m_patterns = new()
        {
            {
                GartleyPatternType.GARTLEY, new GartleyPattern(
                    XBValues: new[] {0.618},
                    XDValues: new[] {0.786},
                    BDValues: new[] {1.13, 1.27, 1.41, 1.618},
                    ACValues: new[] {0.382, 0.5, 0.786, 0.886},
                    SetupType: GartleySetupType.AD)
            },
            {
                GartleyPatternType.BUTTERFLY, new GartleyPattern(
                    XBValues: new[] {0.786},
                    XDValues: new[] {1.27, 1.414},
                    BDValues: new[] {1.618, 2, 2.24},
                    ACValues: new[] {0.382, 0.5, 0.786, 0.886},
                    SetupType: GartleySetupType.AD)
            },//TODO why the heck they aren't the same in all sources
            {
                GartleyPatternType.SHARK, new GartleyPattern(
                    XBValues: Array.Empty<double>(),
                    XDValues: new[] {0.382, 0.5, 0.786, 0.886},
                    BDValues: new[] {1.618, 2, 2.24},
                    ACValues: new[] {1.13, 1.27, 1.41, 1.618},
                    SetupType: GartleySetupType.CD)
            },
            {
                GartleyPatternType.CRAB, new GartleyPattern(
                    XBValues: new[] {0.382, 0.5, 0.786, 0.886},
                    XDValues: new[] {1.618},
                    BDValues: new[] {2.618, 3.272, 3.414, 3.618},
                    ACValues: new[] {1.13, 1.27, 1.41, 1.618},
                    SetupType: GartleySetupType.CD)
            }
        };

        /// <summary>
       /// Initializes a new instance of the <see cref="GartleyPatternFinder"/> class.
       /// </summary>
       /// <param name="shadowAllowance">The correction allowance percent.</param>
       /// <param name="barsProvider">The bars provider.</param>
        public GartleyPatternFinder(double shadowAllowance, IBarsProvider barsProvider)
        {
            m_ShadowAllowance = shadowAllowance;
            m_BarsProvider = barsProvider;
        }

        /// <summary>
        /// Finds the gartley pattern or null if not found.
        /// </summary>
        /// <param name="point">The point (bar) we want to find against.</param>
        /// <returns>Gartley pattern or null</returns>
        public GartleyItem FindGartleyPattern(BarPoint point)
        {
            // , ExtremumFinder extremaFinder
            return null;
        }
    }
}
