using System.Collections.Generic;
using cAlgo.API;
using TradeKit.Core.Common;

namespace TradeKit.CTrader.Core
{
    internal class CTraderStorageManager : IStorageManager
    {
        protected const string STATE_SAVE_KEY = "ReportStateMap";
        protected const string STAT_ALL_SAVE_KEY = "StatisticStateAll";
        protected const string TRADE_KIT_SCHEMA = "TradeKitSchema";
        protected const string TRADE_KIT_SCHEMA_VAL = "2";
        private readonly Robot m_Robot;

        /// <summary>
        /// Initializes a new instance of the <see cref="CTraderStorageManager"/> class.
        /// </summary>
        /// <param name="robot">The robot.</param>
        public CTraderStorageManager(Robot robot)
        {
            m_Robot = robot;
            if (Schema != TRADE_KIT_SCHEMA_VAL) ResetStatistic();
            Schema = TRADE_KIT_SCHEMA_VAL;
        }

        /// <summary>
        /// Gets or sets the schema.
        /// </summary>
        public string Schema
        {
            get => m_Robot.LocalStorage.GetObject<string>(TRADE_KIT_SCHEMA, LocalStorageScope.Device) ?? "0";
            set => m_Robot.LocalStorage.SetObject(TRADE_KIT_SCHEMA, value, LocalStorageScope.Device);
        }

        /// <summary>
        /// Saves the trade state.
        /// </summary>
        /// <param name="stateMap">The state map.</param>
        public void SaveState(Dictionary<string, int> stateMap)
        {
            m_Robot.LocalStorage.SetObject(STATE_SAVE_KEY, stateMap, LocalStorageScope.Device);
        }

        /// <summary>
        /// Gets the saved state dictionary.
        /// </summary>
        public Dictionary<string, int> GetSavedState()
        {
            return m_Robot.LocalStorage.GetObject<Dictionary<string, int>>(STATE_SAVE_KEY);
        }

        private void ResetStatistic()
        {
            m_Robot.LocalStorage.SetObject(STAT_ALL_SAVE_KEY, new StatisticItem(), LocalStorageScope.Device);
        }

        /// <summary>
        /// Gets the last day statistic.
        /// </summary>
        public StatisticItem GetStatistic()
        {
            StatisticItem result = m_Robot.LocalStorage.GetObject<StatisticItem>(STAT_ALL_SAVE_KEY)
                                   ?? new StatisticItem();
            return result;
        }

        /// <summary>
        /// Adds the setup result.
        /// </summary>
        /// <param name="tradeResult">The new result to add.</param>
        public StatisticItem AddSetupResult(double tradeResult)
        {
            StatisticItem result = GetStatistic();
            result.ResultValue += tradeResult;
            result.SetupsCount++;
            m_Robot.LocalStorage.SetObject(STAT_ALL_SAVE_KEY, result, LocalStorageScope.Device);
            return result;
        }
    }
}
