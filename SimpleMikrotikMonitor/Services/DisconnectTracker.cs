using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SimpleMikrotikMonitor.Services
{
    public class DisconnectTracker
    {
        private readonly Dictionary<string, List<DateTime>> _deviceDisconnects = new();
        
        // Track a new disconnect event
        public void TrackDisconnect(string deviceId, DateTime timestamp)
        {
            if (!_deviceDisconnects.ContainsKey(deviceId))
            {
                _deviceDisconnects[deviceId] = new List<DateTime>();
            }
            
            _deviceDisconnects[deviceId].Add(timestamp);
        }
        
        // Check if a device has exceeded the disconnect threshold within the time window
        public bool HasExceededThreshold(string deviceId, int threshold, int timeWindowMinutes)
        {
            if (!_deviceDisconnects.ContainsKey(deviceId))
                return false;
                
            var cutoffTime = DateTime.Now.AddMinutes(-timeWindowMinutes);
            var recentDisconnects = _deviceDisconnects[deviceId]
                .Count(timestamp => timestamp >= cutoffTime);
                
            return recentDisconnects >= threshold;
        }
        
        // Get all devices that have exceeded the threshold
        public List<string> GetDevicesExceedingThreshold(int threshold, int timeWindowMinutes)
        {
            var result = new List<string>();
            var cutoffTime = DateTime.Now.AddMinutes(-timeWindowMinutes);
            
            foreach (var device in _deviceDisconnects.Keys)
            {
                var recentDisconnects = _deviceDisconnects[device]
                    .Count(timestamp => timestamp >= cutoffTime);
                    
                if (recentDisconnects >= threshold)
                {
                    result.Add(device);
                }
            }
            
            return result;
        }
        
        // Get the count of disconnects for a device within the time window
        public int GetDisconnectCount(string deviceId, int timeWindowMinutes)
        {
            if (!_deviceDisconnects.ContainsKey(deviceId))
                return 0;
                
            var cutoffTime = DateTime.Now.AddMinutes(-timeWindowMinutes);
            return _deviceDisconnects[deviceId]
                .Count(timestamp => timestamp >= cutoffTime);
        }
        
        // Clean up old disconnect records
        public void CleanupOldRecords(int maxAgeMinutes)
        {
            var cutoffTime = DateTime.Now.AddMinutes(-maxAgeMinutes);
            
            foreach (var deviceId in _deviceDisconnects.Keys.ToList())
            {
                _deviceDisconnects[deviceId] = _deviceDisconnects[deviceId]
                    .Where(timestamp => timestamp >= cutoffTime)
                    .ToList();
                    
                // Remove the device if it has no recent disconnects
                if (_deviceDisconnects[deviceId].Count == 0)
                {
                    _deviceDisconnects.Remove(deviceId);
                }
            }
        }
        
        // Extract device ID from a log message
        public static string ExtractDeviceId(string logMessage)
        {
            // Try to extract MAC address from common formats
            var macRegex = new Regex(@"([0-9A-F]{2}[:-]){5}([0-9A-F]{2})@?", RegexOptions.IgnoreCase);
            var match = macRegex.Match(logMessage);
            
            if (match.Success)
            {
                return match.Value.TrimEnd('@');
            }
            
            // If no MAC address found, return empty string
            return string.Empty;
        }
    }
}
