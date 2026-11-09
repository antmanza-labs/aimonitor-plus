using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SimpleMikrotikMonitor.Services
{
    public class ScheduleManager
    {
        private readonly AppSettings _settings;

        public ScheduleManager(AppSettings settings)
        {
            _settings = settings;

            // Initialize next scheduled run times for all routers
            // First check right away, staggered 30s apart so routers aren't all polled at the same moment.
            var now = DateTime.Now;
            int slot = 0;
            foreach (var router in _settings.MikrotikSettings.Routers
                         .Where(r => r.IsEnabled && r.Schedule.Enabled)
                         .OrderByDescending(r => r.Schedule.Priority))
            {
                router.Schedule.NextScheduledRun = now.AddSeconds(-10 + 30 * slot++);
            }
        }

        public List<RouterConfig> GetRoutersToCheck()
        {
            var now = DateTime.Now;
            var routersToCheck = new List<RouterConfig>();

            foreach (var router in _settings.MikrotikSettings.Routers)
            {
                if (!router.IsEnabled || !router.Schedule.Enabled)
                    continue;

                // Check if it's time to run this router based on its schedule
                if (IsTimeToRun(router, now))
                {
                    routersToCheck.Add(router);

                    // Update next scheduled run time
                    router.Schedule.NextScheduledRun = CalculateNextRunTime(router, now);
                }
            }

            // Sort by priority (Critical first, then High, Medium, Low)
            return routersToCheck
                .OrderByDescending(r => r.Schedule.Priority)
                .ToList();
        }

        private bool IsTimeToRun(RouterConfig router, DateTime now)
        {
            var schedule = router.Schedule;

            // Check if current time is past the next scheduled run time
            // Use a small buffer (5 seconds) to avoid running too early
            if (now.AddSeconds(-5) < schedule.NextScheduledRun)
            {
                // Debug logging
                Console.WriteLine($"Router {router.Name} not ready yet. Next run at {schedule.NextScheduledRun}, current time is {now}");
                return false;
            }

            // Check if current day is in active days
            if (!schedule.ActiveDays.Contains(now.DayOfWeek))
            {
                // Debug logging
                Console.WriteLine($"Router {router.Name} not scheduled for today ({now.DayOfWeek})");
                return false;
            }

            // Check if current time is within start and end time
            var currentTimeOfDay = now.TimeOfDay;
            if (currentTimeOfDay < schedule.StartTime || currentTimeOfDay > schedule.EndTime)
            {
                // Debug logging
                Console.WriteLine($"Router {router.Name} outside active hours ({schedule.StartTime} - {schedule.EndTime})");
                return false;
            }

            // Log the decision for debugging
            Console.WriteLine($"Router {router.Name} IS scheduled to run at {schedule.NextScheduledRun}, current time is {now}");

            return true;
        }

        private DateTime CalculateNextRunTime(RouterConfig router, DateTime now)
        {
            // Calculate next run time based on interval
            var nextRun = now.AddMinutes(router.Schedule.IntervalMinutes);

            // Debug logging
            Console.WriteLine($"Setting next run time for {router.Name} to {nextRun} (interval: {router.Schedule.IntervalMinutes} min)");

            return nextRun;
        }

        public string GetNextRunInfo(RouterConfig router)
        {
            if (!router.IsEnabled)
                return "Router disabled";

            if (!router.Schedule.Enabled)
                return "Schedule disabled";

            var nextRun = router.Schedule.NextScheduledRun;
            var timeUntilNextRun = nextRun - DateTime.Now;

            if (timeUntilNextRun.TotalMinutes < 1)
                return "Running soon";

            return $"Next run in {(int)timeUntilNextRun.TotalMinutes} min";
        }

        public string GetPriorityDisplay(RouterPriority priority)
        {
            return priority switch
            {
                RouterPriority.Low => "Low",
                RouterPriority.Medium => "Medium",
                RouterPriority.High => "High",
                RouterPriority.Critical => "Critical",
                _ => "Unknown"
            };
        }
    }
}
