using System.Collections.Generic;
using cAlgo.API;

namespace cAlgo
{
    public class ExtremumFinder
    {
        private double m_ExtremumPrice;
        private int m_ExtremumIndex;

        private bool m_IsUpDirection;

        private double DeviationPrice
        {
            get
            {
                double percentRate = m_IsUpDirection ? -0.01 : 0.01;
                return m_ExtremumPrice * (1.0 + DeviationPercent * percentRate);
            }
        }

        private void MoveExtremum(int index, double price)
        {
            Extrema.Remove(m_ExtremumIndex);
            SetExtremum(index, price);
        }

        private void SetExtremum(int index, double price)
        {
            m_ExtremumIndex = index;
            m_ExtremumPrice = price;
            Extrema[m_ExtremumIndex] = m_ExtremumPrice;
        }

        private double DeviationPercent { get; }

        public SortedDictionary<int, double> Extrema { get; }

        public ExtremumFinder(double deviationPercent)
        {
            DeviationPercent = deviationPercent;
            Extrema = new SortedDictionary<int, double>();
        }

        public void Calculate(int index, Bars bars)
        {
            double low = bars.LowPrices[index];
            double high = bars.HighPrices[index];
            if (m_ExtremumPrice == 0.0)
            {
                m_ExtremumPrice = high;
            }

            if (bars.ClosePrices.Count < 2)
            {
                return;
            }

            if (m_IsUpDirection ? high >= m_ExtremumPrice : low <= m_ExtremumPrice)
            {
                MoveExtremum(index, m_IsUpDirection ? high : low);
            }
            else if (m_IsUpDirection ? low <= DeviationPrice : high >= DeviationPrice)
            {
                SetExtremum(index, m_IsUpDirection ? low : high);
                m_IsUpDirection = !m_IsUpDirection;
            }
        }
    }
}
