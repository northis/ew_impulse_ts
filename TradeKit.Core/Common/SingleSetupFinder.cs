using TradeKit.Core.EventArgs;

namespace TradeKit.Core.Common
{
    /// <summary>
    /// Class contains the logic of trade setups searching.
    /// Single-setup version means it support only one setup in the same time.
    /// </summary>
    /// <typeparam name="T">Type of signal events arg</typeparam>
    /// <seealso cref="BaseSetupFinder{T}" />
    public abstract class SingleSetupFinder<T> : BaseSetupFinder<T> where T : SignalEventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SingleSetupFinder{T}"/> class.
        /// </summary>
        /// <param name="mainBarsProvider">The main bars provider.</param>
        /// <param name="symbol">The symbol.</param>
        protected SingleSetupFinder(IBarsProvider mainBarsProvider, ISymbol symbol) 
            : base(mainBarsProvider, symbol)
        {
        }

        /// <summary>
        /// Determines whether the movement from <see cref="startValue"/> to <see cref="endValue"/> is initial.
        /// </summary>
        /// <param name="startValue">The start value.</param>
        /// <param name="endValue">The end value.</param>
        /// <param name="startIndex">The start index.</param>
        /// <param name="bp">The source bars provider.</param>
        /// <param name="edgeExtremum">The extremum from the end of the movement to the previous counter-movement or how far this movement went away from the price channel.</param>
        /// <returns>
        ///   <c>true</c> if the move is initial; otherwise, <c>false</c>.
        /// </returns>
        protected bool IsInitialMovement(
            double startValue, 
            double endValue, 
            int startIndex, 
            IBarsProvider bp,
            out Candle edgeExtremum)
        {
            // We want to rewind the bars to be sure this impulse candidate is really an initial one
            bool isInitialMove = false;
            bool isImpulseUp = endValue > startValue;
            edgeExtremum = null;

            for (int curIndex = startIndex - 1; curIndex >= 0; curIndex--)
            {
                edgeExtremum = Candle.FromIndex(bp, curIndex);

                if (isImpulseUp)
                {
                    if (edgeExtremum.L <= startValue)
                    {
                        break;
                    }

                    if (edgeExtremum.H - endValue > 0)
                    {
                        isInitialMove = true;
                        break;
                    }

                    continue;
                }

                if (edgeExtremum.H >= startValue)
                {
                    break;
                }

                if (!(edgeExtremum.L - endValue < 0))
                {
                    continue;
                }

                isInitialMove = true;
                break;
            }

            return isInitialMove;
        }

        /// <summary>
        /// Gets or sets a value indicating whether this finder is in setup.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is in setup; otherwise, <c>false</c>.
        /// </value>
        public bool IsInSetup { get; set; }
    }
}
