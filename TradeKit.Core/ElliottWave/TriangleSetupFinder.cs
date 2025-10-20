using System.Diagnostics;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Indicators;

namespace TradeKit.Core.ElliottWave
{
    /// <summary>
    /// Class contains the EW ABCDE-triangle logic of trade setups searching.
    /// </summary>
    public class TriangleSetupFinder : SingleSetupFinder<ElliottWaveSignalEventArgs>
    {
        private readonly EWParams m_EwParams;
        private readonly List<DeviationExtremumFinder> m_ExtremumFinders = new();

        private const double RATIO_ALLOWANCE = 0.1;
        private const double MAX_D_TO_E_RATIO = 1.618 + RATIO_ALLOWANCE;
        private const double MAX_C_TO_E_RATIO = 2 + RATIO_ALLOWANCE;
        private const double MAX_B_TO_E_RATIO = 2.618 + RATIO_ALLOWANCE;
        private const double MAX_A_TO_E_RATIO = 3.618 + RATIO_ALLOWANCE;

        /// <summary>
        /// Implements the logic for searching trade setups based on the EW ABCDE-triangle pattern.
        /// </summary>
        public TriangleSetupFinder(IBarsProvider mainBarsProvider,
            ISymbol symbol, EWParams ewParams) : base(mainBarsProvider, symbol)
        {
            m_EwParams = ewParams;

            for (int i = ewParams.Period; i <= ewParams.Period * 4; i += 10)
            {
                var localFinder = new DeviationExtremumFinder(i, BarsProvider);
                m_ExtremumFinders.Add(localFinder);
            }
        }

        /// <summary>
        /// Checks whether a setup condition is satisfied at the specified open date and time.
        /// </summary>
        /// <param name="openDateTime">The open date and time to check the setup against.</param>
        protected override void CheckSetup(DateTime openDateTime)
        {
            foreach (DeviationExtremumFinder finder in m_ExtremumFinders
                         .OrderByDescending(a => a.ScaleRate))
            {
                finder.OnCalculate(openDateTime);
                if (!IsInitialized)
                    continue;

                if (IsSetup(openDateTime, finder))
                {
                    break;
                }
            }
        }

        private bool IsMovementForward(bool isUp, BarPoint currentExtremum,
            BarPoint compareExtremum)
        {
            return isUp && currentExtremum > compareExtremum ||
                   !isUp && currentExtremum < compareExtremum;
        }

        private bool IsSetup(DateTime openDateTime,
            DeviationExtremumFinder finder)
        {
            if (finder.Extrema.Count < MIN_EXTREMUM_COUNT)
                return false;

            BarPoint waveE = finder.Extrema.Values[^1];
            BarPoint waveD = finder.Extrema.Values[^2];
            BarPoint waveC = null;
            BarPoint waveB = null;
            BarPoint waveA = null;
            BarPoint point0 = null;
            bool isUp = waveD > waveE;

            for (int i = finder.Extrema.Count - 3;
                 i > Math.Max(0, finder.Extrema.Count - MAX_EXTREMA_DEPTH);
                 i--)
            {
                BarPoint currentExtremum = finder.Extrema.Values[i];
                double dToE = Math.Abs(waveD - waveE);
                if (dToE == 0)
                    return false;

                if (waveC == null)
                {
                    if (IsMovementForward(isUp, currentExtremum, waveD))
                    {
                        waveD = currentExtremum;
                        continue;
                    }

                    if (!IsMovementForward(isUp, currentExtremum, waveE))
                    {
                        waveC = currentExtremum;
                        if (Math.Abs(waveD - waveC) / dToE > MAX_D_TO_E_RATIO)
                            return false;
                    }

                    continue;
                }

                if (waveB == null)
                {
                    if (!IsMovementForward(isUp, currentExtremum, waveC))
                    {
                        waveC = currentExtremum;
                        if (Math.Abs(waveD - waveC) / dToE > MAX_D_TO_E_RATIO)
                            return false;

                        continue;
                    }

                    if (IsMovementForward(isUp, currentExtremum, waveD))
                    {
                        waveB = currentExtremum;
                        if (Math.Abs(waveC - waveB) / dToE > MAX_C_TO_E_RATIO)
                            return false;
                    }

                    continue;
                }

                if (waveA == null)
                {
                    if (IsMovementForward(isUp, currentExtremum, waveB))
                    {
                        waveB = currentExtremum;
                        if (Math.Abs(waveB - waveC) / dToE > MAX_C_TO_E_RATIO)
                            return false;

                        continue;
                    }

                    if (!IsMovementForward(isUp, currentExtremum, waveC))
                    {
                        waveA = currentExtremum;
                        if (Math.Abs(waveB - waveA) / dToE > MAX_B_TO_E_RATIO)
                            return false;
                    }

                    continue;
                }

                if (point0 == null)
                {
                    if (!IsMovementForward(isUp, currentExtremum, waveA))
                    {
                        waveA = currentExtremum;
                        if (Math.Abs(waveA - waveB) / dToE > MAX_B_TO_E_RATIO)
                            return false;
                    }

                    if (IsMovementForward(isUp, currentExtremum, waveB))
                    {
                        point0 = currentExtremum;
                        if (Math.Abs(waveA - point0) / dToE > MAX_A_TO_E_RATIO)
                            return false;
                    }
                }
                else
                {
                    var level = new BarPoint(isUp
                            ? BarsProvider.GetLowPrice(openDateTime)
                            : BarsProvider.GetHighPrice(openDateTime),
                        openDateTime,
                        BarsProvider);

                    OnEnterInvoke(new ElliottWaveSignalEventArgs(level, point0,
                        waveA,
                        new[] { point0, waveA, waveB, waveC, waveD, waveE },
                        point0.OpenTime, null));
                    return true;
                }

                // if (IsMovementForward(isUp, currentExtremum, waveB))
                // {
                //     point0 = currentExtremum;
                //     if (Math.Abs(waveA - point0) / dToE > MAX_A_TO_E_RATIO)
                //         return false;
                // }
            }

            return false;
        }

        private const int MAX_EXTREMA_DEPTH = 100;
        private const int MIN_EXTREMUM_COUNT = 6;
    }
}
