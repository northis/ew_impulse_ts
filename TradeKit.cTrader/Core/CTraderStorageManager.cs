using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using TradeKit.Core.Common;

namespace TradeKit.CTrader.Core
{
    internal class CTraderStorageManager : IStorageManager
    {
        protected const string STATE_SAVE_KEY = "ReportStateMap";
        protected const string STAT_SAVE_KEY = "StatisticState";
        protected const string STAT_DAILY_SAVE_KEY = "StatisticDailyState";
        private readonly Robot m_Robot;

        /// <summary>
        /// Initializes a new instance of the <see cref="CTraderStorageManager"/> class.
        /// </summary>
        /// <param name="robot">The robot.</param>
        public CTraderStorageManager(Robot robot)
        {
            m_Robot = robot;
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

        /// <summary>
        /// Gets the statistic items.
        /// </summary>
        private List<StatisticItem> GetStatisticItems()
        {
            List<StatisticItem> result =
                m_Robot.LocalStorage.GetObject<List<StatisticItem>>(STAT_SAVE_KEY) ??
                new List<StatisticItem>();
            return result;
        }

        /// <summary>
        /// Gets the last day statistic.
        /// </summary>
        private StatisticItem GetStatisticLastDay()
        {
            StatisticItem result = m_Robot.LocalStorage.GetObject<StatisticItem>(STAT_DAILY_SAVE_KEY) ??
                                   new StatisticItem();
            return result;
        }

        /// <summary>
        /// Adds the setup result.
        /// </summary>
        /// <param name="statItem">The stat item.</param>
        /// <returns></returns>
        public StatisticItem AddSetupResult(StatisticItem statItem)
        {
            DateTime dt = DateTime.UtcNow;
            DateTime dayStart = dt.Add(-dt.TimeOfDay);
            List<StatisticItem> existedItems = GetStatisticItems();
            StatisticItem last = existedItems.Last();
            if (last.CloseDateTime.Day != statItem.CloseDateTime.Day ||
                dayStart < last.CloseDateTime)
            {
                existedItems.Add(statItem);
                last = statItem;
            }
            else
            {
                last.ResultPercent += statItem.ResultPercent;
                last.ResultPips += statItem.ResultPips;
            }

            m_Robot.LocalStorage.SetObject(STAT_SAVE_KEY, existedItems, LocalStorageScope.Device);
            return last;
        }

        /// <summary>
        /// Gets the latest.
        /// </summary>
        /// <param name="period">The period.</param>
        public StatisticItem GetLatest(TimeSpan period)
        {
            DateTime threshold = DateTime.UtcNow.Add(-period);
            StatisticItem lastDay = GetStatisticLastDay();
            if (period < TimeSpan.FromDays(1))
            {
                return lastDay;
            }

            List<StatisticItem> currentItems = GetStatisticItems();
            List<StatisticItem> latest = currentItems
                .Where(a => a.CloseDateTime >= threshold)
                .ToList();

            var result = new StatisticItem
            {
                CloseDateTime = threshold,
                ResultPercent = latest.Sum(a => a.ResultPercent) + lastDay.ResultPercent,
                ResultPips = latest.Sum(a => a.ResultPips) + lastDay.ResultPips
            };

            return result;
        }
    }
}
