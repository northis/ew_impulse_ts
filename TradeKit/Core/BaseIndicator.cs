using System;
using cAlgo.API;
using TradeKit.EventArgs;

namespace TradeKit.Core
{
    /// <summary>
    /// Base indicator for Setup finders
    /// </summary>
    /// <typeparam name="T">Type of the setup finder</typeparam>
    /// <typeparam name="TK">The type of the signal argument.</typeparam>
    /// <seealso cref="cAlgo.API.Indicator" />
    public abstract class BaseIndicator<T, TK> 
        : Indicator where T : BaseSetupFinder<TK> where TK : SignalEventArgs
    {
        private bool m_IsInitialized;
        private T m_SetupFinder;

        /// <summary>
        /// Subscribes to the events of the setup finder passed.
        /// </summary>
        /// <param name="setupFinder">The setup finder.</param>
        protected void Subscribe(T setupFinder)
        {
            m_SetupFinder = setupFinder;
            m_SetupFinder.OnEnter += OnEnter;
            m_SetupFinder.OnStopLoss += OnStopLoss;
            m_SetupFinder.OnTakeProfit += OnTakeProfit;
            m_SetupFinder.OnTakeProfit += OnBreakeven;
        }
        
        /// <summary>
        /// Gets or sets a value indicating whether we should show possible SL and TP for each pattern.
        /// </summary>
        [Parameter(nameof(ShowSetups), DefaultValue = true, Group = Helper.VIEW_SETTINGS_NAME)]
        public bool ShowSetups { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the logging is enabled.
        /// </summary>
        [Parameter(nameof(EnableLog), DefaultValue = true, Group = Helper.VIEW_SETTINGS_NAME)]
        public bool EnableLog { get; set; }

        /// <summary>
        /// Called when stop event loss occurs.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected abstract void OnStopLoss(object sender, LevelEventArgs e);

        /// <summary>
        /// Called when take profit event occurs.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected abstract void OnTakeProfit(object sender, LevelEventArgs e);

        /// <summary>
        /// Called when breakeven event occurs.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected virtual void OnBreakeven(object sender, LevelEventArgs e)
        {

        }

        /// <summary>
        /// Called on new signal.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The event argument type.</param>
        protected abstract void OnEnter(object sender, TK e);

        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        /// <exception cref="NotSupportedException">Time frame {TimeFrame} isn't supported.</exception>
        protected override void Initialize()
        {
            if (!TimeFrameHelper.TimeFrames.ContainsKey(TimeFrame))
            {
                throw new NotSupportedException(
                    $"Time frame {TimeFrame} isn't supported.");
            }

            if (EnableLog)
                Logger.SetWrite(a => Print(a));

            base.Initialize();
        }

        /// <summary>
        /// Called when Indicator is destroyed.
        /// </summary>
        protected override void OnDestroy()
        {
            m_SetupFinder.OnEnter -= OnEnter;
            m_SetupFinder.OnStopLoss -= OnStopLoss;
            m_SetupFinder.OnTakeProfit -= OnTakeProfit;
            m_SetupFinder.OnTakeProfit -= OnBreakeven;
            base.OnDestroy();
        }

        /// <summary>
        /// Calculate the value(s) of indicator for the given index.
        /// </summary>
        /// <param name="index">The index of calculated value.</param>
        public override void Calculate(int index)
        {
            if (m_SetupFinder == null)
                return;
            //throw new InvalidOperationException("Please, call Subscribe() first");

            m_SetupFinder.CheckBar(m_IsInitialized ? index - 1 : index);
            if (IsLastBar && !m_IsInitialized)
            {
                m_IsInitialized = true;
                Logger.Write($"History ok, index {index}");
            }
        }
    }
}
