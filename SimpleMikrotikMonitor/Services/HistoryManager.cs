using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SimpleMikrotikMonitor.Services
{
    public class HistoryManager
    {
        private readonly AppSettings _settings;
        private readonly int _maxHistoryEntriesPerRouter = 100;

        public HistoryManager(AppSettings settings)
        {
            _settings = settings;
        }

        public void AddHistoryEntry(RouterConfig router, bool success, string message, int logCount = 0, bool hasAlerts = false)
        {
            var entry = new RouterHistoryEntry
            {
                Timestamp = DateTime.Now,
                Success = success,
                Message = message,
                LogCount = logCount,
                HasAlerts = hasAlerts
            };

            // Add to router's history
            router.History.Add(entry);

            // Trim history if it exceeds the maximum
            if (router.History.Count > _maxHistoryEntriesPerRouter)
            {
                router.History = router.History
                    .OrderByDescending(h => h.Timestamp)
                    .Take(_maxHistoryEntriesPerRouter)
                    .ToList();
            }
        }

        public List<RouterHistoryEntry> GetHistoryForRouter(RouterConfig router)
        {
            if (router == null || router.History == null)
            {
                return new List<RouterHistoryEntry>
                {
                    new RouterHistoryEntry
                    {
                        Timestamp = DateTime.Now,
                        Success = false,
                        Message = "Error: Router or history is null",
                        LogCount = 0,
                        HasAlerts = false
                    }
                };
            }

            // Initialize history if it's null
            if (router.History == null)
            {
                router.History = new List<RouterHistoryEntry>();
                router.History.Add(new RouterHistoryEntry
                {
                    Timestamp = DateTime.Now,
                    Success = true,
                    Message = "History initialized",
                    LogCount = 0,
                    HasAlerts = false
                });
            }

            return router.History
                .OrderByDescending(h => h.Timestamp)
                .ToList();
        }

        public List<RouterHistoryEntry> GetAllHistory()
        {
            var allHistory = new List<RouterHistoryEntry>();

            // Add a debug entry to ensure we're getting something
            allHistory.Add(new RouterHistoryEntry
            {
                Timestamp = DateTime.Now,
                Success = true,
                Message = "Debug: GetAllHistory called",
                LogCount = 0,
                HasAlerts = false
            });

            try
            {
                if (_settings.MikrotikSettings.Routers != null)
                {
                    foreach (var router in _settings.MikrotikSettings.Routers)
                    {
                        if (router.History == null)
                        {
                            router.History = new List<RouterHistoryEntry>();
                            router.History.Add(new RouterHistoryEntry
                            {
                                Timestamp = DateTime.Now,
                                Success = true,
                                Message = "History initialized",
                                LogCount = 0,
                                HasAlerts = false
                            });
                        }

                        // Add router name to message for context
                        var routerHistory = router.History.Select(h =>
                        {
                            var entryCopy = new RouterHistoryEntry
                            {
                                Timestamp = h.Timestamp,
                                Success = h.Success,
                                Message = $"[{router.Name}] {h.Message}",
                                LogCount = h.LogCount,
                                HasAlerts = h.HasAlerts
                            };
                            return entryCopy;
                        });

                        allHistory.AddRange(routerHistory);
                    }
                }
            }
            catch (Exception ex)
            {
                // Add error entry
                allHistory.Add(new RouterHistoryEntry
                {
                    Timestamp = DateTime.Now,
                    Success = false,
                    Message = $"Error getting history: {ex.Message}",
                    LogCount = 0,
                    HasAlerts = false
                });
            }

            return allHistory
                .OrderByDescending(h => h.Timestamp)
                .ToList();
        }

        public void ClearHistoryForRouter(RouterConfig router)
        {
            router.History.Clear();
        }

        public void ClearAllHistory()
        {
            foreach (var router in _settings.MikrotikSettings.Routers)
            {
                router.History.Clear();
            }
        }
    }
}
