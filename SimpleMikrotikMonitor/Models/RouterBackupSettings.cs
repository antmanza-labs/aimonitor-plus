using System;
using System.Collections.Generic;

namespace SimpleMikrotikMonitor.Models
{
    /// <summary>
    /// Settings for router backup functionality
    /// </summary>
    public class RouterBackupSettings
    {
        /// <summary>
        /// Whether automatic backups are enabled
        /// </summary>
        public bool EnableAutoBackups { get; set; } = false;

        /// <summary>
        /// Backup interval in days
        /// </summary>
        public int BackupIntervalDays { get; set; } = 7;

        /// <summary>
        /// Maximum number of backups to keep per router
        /// </summary>
        public int MaxBackupsPerRouter { get; set; } = 10;

        /// <summary>
        /// Custom backup directory path (if empty, defaults to app directory/router_backups)
        /// </summary>
        public string BackupDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Whether to include sensitive data in backups (passwords, etc.)
        /// </summary>
        public bool IncludeSensitiveData { get; set; } = false;

        /// <summary>
        /// Next scheduled backup time
        /// </summary>
        public DateTime NextScheduledBackup { get; set; } = DateTime.Now.AddDays(7);

        /// <summary>
        /// List of routers to exclude from automatic backups
        /// </summary>
        public List<string> ExcludedRouters { get; set; } = new List<string>();
    }

    /// <summary>
    /// Information about a router backup
    /// </summary>
    public class RouterBackupInfo
    {
        /// <summary>
        /// Router name
        /// </summary>
        public string RouterName { get; set; } = string.Empty;

        /// <summary>
        /// Router IP address
        /// </summary>
        public string RouterIP { get; set; } = string.Empty;

        /// <summary>
        /// Backup creation time
        /// </summary>
        public DateTime CreationTime { get; set; } = DateTime.Now;

        /// <summary>
        /// Backup file path
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// Backup file size in bytes
        /// </summary>
        public long FileSize { get; set; } = 0;

        /// <summary>
        /// Whether the backup includes sensitive data
        /// </summary>
        public bool IncludesSensitiveData { get; set; } = false;

        /// <summary>
        /// Backup type (manual or scheduled)
        /// </summary>
        public BackupType Type { get; set; } = BackupType.Manual;

        /// <summary>
        /// Optional notes about the backup
        /// </summary>
        public string Notes { get; set; } = string.Empty;
    }

    /// <summary>
    /// Type of backup
    /// </summary>
    public enum BackupType
    {
        Manual,
        Scheduled
    }
}
