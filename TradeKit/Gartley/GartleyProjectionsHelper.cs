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
    
    internal record RealLevelCombo(RealLevel Xd, RealLevel Bd)
    {
        public double Max { get; init; } = Math.Min(Xd.Max, Bd.Max);
        public double Min { get; init; } = Math.Max(Xd.Min, Bd.Min);
    }
}
