using System;
using cAlgo.API;

namespace TradeKit.Core
{
    /// <summary>
    /// Contains the bar point data
    /// </summary>
    public record BarPoint : IComparable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BarPoint"/> class.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="openTime">The open time.</param>
        /// <param name="barTimeFrame">The bar time frame.</param>
        /// <param name="barIndex">Index of the bar.</param>
        public BarPoint(double value, DateTime openTime, TimeFrame barTimeFrame, int barIndex)
        {
            Value = value;
            OpenTime = openTime;
            BarTimeFrame = barTimeFrame;
            BarIndex = barIndex;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BarPoint"/> class.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="provider">The bar provider we base on.</param>
        /// <param name="barIndex">Index of the bar.</param>
        public BarPoint(double value, int barIndex, IBarsProvider provider):this(value, provider.GetOpenTime(barIndex), provider.TimeFrame, barIndex)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BarPoint"/> class.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="provider">The bar provider we base on.</param>
        /// <param name="openTime">The open time.</param>
        public BarPoint(double value, DateTime openTime, IBarsProvider provider) : this(value, openTime, provider.TimeFrame, provider.GetIndexByTime(openTime))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BarPoint"/> class.
        /// </summary>
        /// <param name="provider">The bar provider we base on.</param>
        /// <param name="barIndex">Index of the bar.</param>
        public BarPoint(int barIndex, IBarsProvider provider) : this(provider.GetClosePrice(barIndex),
            provider.GetOpenTime(barIndex), provider.TimeFrame, barIndex)
        {
        }

        /// <inheritdoc cref="object"/>
        public override int GetHashCode()
        {
            return HashCode.Combine(Value, OpenTime, BarTimeFrame);
        }

        /// <summary>
        /// Gets the value of the extremum.
        /// </summary>
        public double Value { get; }

        /// <summary>
        /// Gets open time of the bar with this extremum.
        /// </summary>
        public DateTime OpenTime { get; }

        /// <summary>
        /// Gets time frame of the bar.
        /// </summary>
        public TimeFrame BarTimeFrame { get; }

        /// <summary>
        /// Gets the index of the bar.
        /// </summary>
        public int BarIndex { get; }

        /// <summary>
        /// Implements the operator &gt;=.
        /// </summary>
        /// <param name="a">a.</param>
        /// <param name="b">The b.</param>
        /// <returns>
        /// The result of the operator.
        /// </returns>
        public static bool operator >=(BarPoint a, BarPoint b)
        {
            if (a is null || b is null) return false;
            return a.Value >= b.Value;
        }

        /// <summary>
        /// Implements the operator &lt;=.
        /// </summary>
        /// <param name="a">a.</param>
        /// <param name="b">The b.</param>
        /// <returns>
        /// The result of the operator.
        /// </returns>
        public static bool operator <=(BarPoint a, BarPoint b)
        {
            if (a is null || b is null) return false;
            return a.Value <= b.Value;
        }

        /// <summary>
        /// Implements the operator &gt;.
        /// </summary>
        /// <param name="a">a.</param>
        /// <param name="b">The b.</param>
        /// <returns>
        /// The result of the operator.
        /// </returns>
        public static bool operator >(BarPoint a, BarPoint b)
        {
            if (a is null || b is null) return false;
            return a.Value > b.Value;
        }

        /// <summary>
        /// Implements the operator &gt;.
        /// </summary>
        /// <param name="a">a.</param>
        /// <param name="b">The b.</param>
        /// <returns>
        /// The result of the operator.
        /// </returns>
        public static bool operator >(double a, BarPoint b)
        {
            if (b is null) return false;
            return a > b.Value;
        }

        /// <summary>
        /// Implements the operator &lt;.
        /// </summary>
        /// <param name="a">a.</param>
        /// <param name="b">The b.</param>
        /// <returns>
        /// The result of the operator.
        /// </returns>
        public static bool operator <(BarPoint a, BarPoint b)
        {
            if (a is null || b is null) return false;
            return a.Value < b.Value;
        }

        /// <summary>
        /// Implements the operator &lt;.
        /// </summary>
        /// <param name="a">a.</param>
        /// <param name="b">The b.</param>
        /// <returns>
        /// The result of the operator.
        /// </returns>
        public static bool operator <(double a, BarPoint b)
        {
            if (b is null) return false;
            return a < b.Value;
        }

        /// <summary>
        /// Implements the operator +.
        /// </summary>
        /// <param name="a">a.</param>
        /// <param name="b">The b.</param>
        /// <returns>
        /// The result of the operator.
        /// </returns>
        public static double operator +(BarPoint a, BarPoint b)
        {
            if (a is null || b is null) return 0;
            return a.Value + b.Value;
        }

        /// <summary>
        /// Implements the operator +.
        /// </summary>
        /// <param name="a">a.</param>
        /// <param name="b">The b.</param>
        /// <returns>
        /// The result of the operator.
        /// </returns>
        public static double operator +(double a, BarPoint b)
        {
            if (b is null) return a;
            return a + b.Value;
        }

        /// <summary>
        /// Implements the operator -.
        /// </summary>
        /// <param name="a">a.</param>
        /// <param name="b">The b.</param>
        /// <returns>
        /// The result of the operator.
        /// </returns>
        public static double operator -(BarPoint a, BarPoint b)
        {
            if (a is null || b is null) return 0;
            return a.Value - b.Value;
        }

        /// <summary>
        /// Implements the operator -.
        /// </summary>
        /// <param name="a">a.</param>
        /// <param name="b">The b.</param>
        /// <returns>
        /// The result of the operator.
        /// </returns>
        public static double operator -(double a, BarPoint b)
        {
            if (b is null) return a;
            return a - b.Value;
        }

        /// <summary>
        /// Implements the operator ==.
        /// </summary>
        /// <param name="a">a.</param>
        /// <param name="b">The b.</param>
        /// <returns>
        /// The result of the operator.
        /// </returns>
        public static bool operator ==(double a, BarPoint b)
        {
            return b is not null && Math.Abs(a - b.Value) < double.Epsilon;
        }

        /// <summary>
        /// Implements the operator !=.
        /// </summary>
        /// <param name="a">a.</param>
        /// <param name="b">The b.</param>
        /// <returns>
        /// The result of the operator.
        /// </returns>
        public static bool operator !=(double a, BarPoint b)
        {
            return !(a == b);
        }

        /// <inheritdoc cref="IComparable"/>
        public int CompareTo(object obj)
        {
            BarPoint compareExtremum = (BarPoint)obj;
            if (Equals(compareExtremum))
            {
                return 0;
            }

            return this > compareExtremum ? 1 : -1;
        }
    }
}
