using System;
using System.Collections.Generic;
using cAlgo.API;
using TradeKit.Core;
using TradeKit.Json;

namespace TradeKit.PatternGeneration
{
    public class PatternManager
    {
        private readonly PatternGenerator m_Generator;

        public PatternManager(PatternGenerator generator)
        {
            m_Generator = generator;
        }

        //public List<JsonCandleExport> GetCandles(
        //    DateTime start, DateTime end, TimeFrame tf)
        //{

        //}
    }
}
