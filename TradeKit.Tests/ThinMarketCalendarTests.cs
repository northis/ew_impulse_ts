using NUnit.Framework;
using TradeKit.Core.Common;

namespace TradeKit.Tests
{
    /// <summary>
    /// Tests of <see cref="ThinMarketCalendar"/> (DIAGONAL.md §4.7): the thin window is the
    /// break between two trading sessions widened by the margins — the daily rollover break for
    /// an instrument quoted around the clock, the weekend for a continuous FX schedule, an
    /// intraday pause for an exchange-traded one.
    /// </summary>
    internal class ThinMarketCalendarTests
    {
        /// <summary>Monday.</summary>
        private static readonly DateTime MONDAY = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

        private static DateTime At(int dayOffset, int hour, int minute = 0)
        {
            return MONDAY.AddDays(dayOffset).AddHours(hour).AddMinutes(minute);
        }

        private static ThinMarketCalendar Daily(
            string breakStart, string breakEnd, int preMinutes = 15, int postMinutes = 15)
        {
            return new ThinMarketCalendar(
                Array.Empty<ITradingHours>(),
                TimeSpan.FromMinutes(preMinutes),
                TimeSpan.FromMinutes(postMinutes),
                ThinMarketCalendar.ParseTimeOfDay(breakStart),
                ThinMarketCalendar.ParseTimeOfDay(breakEnd));
        }

        [Test]
        public void DailyBreak_WindowStartsBeforeTheCloseAndEndsAfterTheOpen()
        {
            ThinMarketCalendar calendar = Daily("21:00", "22:00");

            Assert.That(calendar.IsEnabled, Is.True);
            Assert.That(calendar.IsThinMarket(At(0, 20, 44)), Is.False, "before the window");
            Assert.That(calendar.IsThinMarket(At(0, 20, 45)), Is.True, "15 minutes before the break");
            Assert.That(calendar.IsThinMarket(At(0, 21, 30)), Is.True, "inside the break");
            Assert.That(calendar.IsThinMarket(At(0, 22, 14)), Is.True, "15 minutes after the open");
            Assert.That(calendar.IsThinMarket(At(0, 22, 15)), Is.False, "the market is thick again");
            Assert.That(calendar.IsThinMarket(At(3, 12, 0)), Is.False, "the middle of the day");
        }

        [Test]
        public void DailyBreak_RepeatsEveryDay()
        {
            ThinMarketCalendar calendar = Daily("21:00", "22:00");

            for (int day = 0; day < 5; day++)
            {
                Assert.That(calendar.IsThinMarket(At(day, 21, 30)), Is.True, $"day {day}");
                Assert.That(calendar.IsThinMarket(At(day, 19, 0)), Is.False, $"day {day}");
            }
        }

        [Test]
        public void DailyBreak_CrossingMidnight()
        {
            ThinMarketCalendar calendar = Daily("23:30", "00:30");

            Assert.That(calendar.IsThinMarket(At(1, 23, 20)), Is.True, "before the close");
            Assert.That(calendar.IsThinMarket(At(2, 0, 0)), Is.True, "inside the break");
            Assert.That(calendar.IsThinMarket(At(2, 0, 44)), Is.True, "after the open");
            Assert.That(calendar.IsThinMarket(At(2, 0, 45)), Is.False, "the market is thick again");
        }

        [Test]
        public void InstantBreak_MarginsAloneMakeTheWindow()
        {
            // A broker that pauses for no measurable time still thins out around the rollover.
            ThinMarketCalendar calendar = Daily("21:00", "21:00", preMinutes: 15, postMinutes: 15);

            Assert.That(calendar.IsThinMarket(At(0, 20, 50)), Is.True);
            Assert.That(calendar.IsThinMarket(At(0, 21, 10)), Is.True);
            Assert.That(calendar.IsThinMarket(At(0, 21, 20)), Is.False);
        }

        [Test]
        public void ZeroMargins_InstantBreakHasNoWindow()
        {
            ThinMarketCalendar calendar = Daily("21:00", "21:00", preMinutes: 0, postMinutes: 0);

            Assert.That(calendar.IsThinMarket(At(0, 21, 0)), Is.False);
        }

        [Test]
        public void WeeklySession_TheWeekendIsOneBreak()
        {
            // What a broker reports for FX: one session from Monday to Friday evening.
            var calendar = new ThinMarketCalendar(
                new ITradingHours[]
                {
                    new TestTradingHours(DayOfWeek.Monday, DayOfWeek.Friday,
                        TimeSpan.Zero, new TimeSpan(22, 0, 0))
                },
                TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15));

            Assert.That(calendar.IsEnabled, Is.True);
            Assert.That(calendar.IsThinMarket(At(4, 21, 50)), Is.True, "Friday, before the close");
            Assert.That(calendar.IsThinMarket(At(4, 21, 40)), Is.False, "Friday, still thick");
            Assert.That(calendar.IsThinMarket(At(5, 12, 0)), Is.True, "Saturday");
            Assert.That(calendar.IsThinMarket(At(7, 0, 10)), Is.True, "Monday, after the open");
            Assert.That(calendar.IsThinMarket(At(7, 0, 20)), Is.False, "Monday, thick again");
            Assert.That(calendar.IsThinMarket(At(2, 12, 0)), Is.False, "Wednesday");
        }

        [Test]
        public void DailySessions_TheIntradayPauseIsABreak()
        {
            // An exchange-traded schedule: a session per weekday with an hour of closure.
            ITradingHours[] sessions = Enumerable.Range(1, 5)
                .Select(day => (ITradingHours)new TestTradingHours(
                    (DayOfWeek)day, (DayOfWeek)day, TimeSpan.Zero, new TimeSpan(23, 0, 0)))
                .ToArray();

            var calendar = new ThinMarketCalendar(
                sessions, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15));

            Assert.That(calendar.IsThinMarket(At(1, 22, 50)), Is.True, "Tuesday, before the close");
            Assert.That(calendar.IsThinMarket(At(1, 23, 30)), Is.True, "Tuesday, the pause");
            Assert.That(calendar.IsThinMarket(At(2, 0, 10)), Is.True, "Wednesday, after the open");
            Assert.That(calendar.IsThinMarket(At(2, 0, 20)), Is.False, "Wednesday, thick again");
            Assert.That(calendar.IsThinMarket(At(1, 12, 0)), Is.False, "Tuesday, midday");
        }

        [Test]
        public void NoSchedule_Disabled()
        {
            var calendar = new ThinMarketCalendar(
                Array.Empty<ITradingHours>(), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15));

            Assert.That(calendar.IsEnabled, Is.False);
            Assert.That(calendar.IsThinMarket(At(0, 21, 30)), Is.False);
        }

        [TestCase("21:00", "21:00:00")]
        [TestCase("21:00:00", "21:00:00")]
        [TestCase(" 21:00 ", "21:00:00")]
        [TestCase("", null)]
        [TestCase(null, null)]
        [TestCase("nonsense", null)]
        public void ParseTimeOfDay(string? value, string? expected)
        {
            TimeSpan? parsed = ThinMarketCalendar.ParseTimeOfDay(value!);
            Assert.That(parsed?.ToString(), Is.EqualTo(expected));
        }
    }
}
