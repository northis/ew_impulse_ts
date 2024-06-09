using System;

namespace TradeKit.Gartley
{
    internal enum ProjectionState
    {
        /// <summary>
        /// No projection found
        /// </summary>
        NO_PROJECTION,
        /// <summary>
        /// The projection has been formed
        /// </summary>
        PROJECTION_FORMED,
        /// <summary>
        /// The projection remains the same as in the previous update
        /// </summary>
        PROJECTION_SAME,
        /// <summary>
        /// The pattern has been formed
        /// </summary>
        PATTERN_FORMED,
        /// <summary>
        /// The pattern remains the same as in the previous update
        /// </summary>
        PATTERN_SAME
    }

    internal enum CalculationState
    {
        A_TO_B,
        B_TO_C,
        A_TO_C,
        C_TO_D,
        D,
        NONE
    }

    internal record RealLevelBase(double StartValue, double EndValue)
    {
        public readonly double Max = Math.Max(StartValue, EndValue);
        public readonly double Min = Math.Min(StartValue, EndValue);

        public double StartValue { get; } = StartValue;
        public double EndValue { get; } = EndValue;
    }

    internal record RealLevel(double Ratio, double StartValue, double EndValue)
        : RealLevelBase(StartValue, EndValue);
    
    internal record RealLevelCombo
    {
        public RealLevelCombo(RealLevel xD, RealLevel bD)
        {
            Xd = xD;
            Bd = bD;
            Max = Math.Min(xD.Max, bD.Max);
            Min = Math.Max(xD.Min, bD.Min);
            IsMaxXd = Math.Abs(xD.Max - Max) < double.Epsilon;
            IsMinXd = Math.Abs(xD.Min - Min) < double.Epsilon;
        }

        public bool IsMaxXd { get; init; }
        public bool IsMinXd { get; init; }

        public RealLevel Xd { get; init; }
        public RealLevel Bd { get; init; }
        public double Max { get; init; }
        public double Min { get; init; }
    }
}
