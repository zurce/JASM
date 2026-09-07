namespace GIMI_ModManager.WinUI.Helpers
{
    public static class FormaterHelpers
    {
        public static string FormatTimeSinceAdded(TimeSpan timeSinceAdded)
        {
            return timeSinceAdded switch
            {
                { Days: > 0 } => $"{Math.Round(timeSinceAdded.TotalDays)} days ago",
                { Hours: > 0 } => $"{timeSinceAdded.Hours} hours ago",
                { Minutes: > 0 } => $"{timeSinceAdded.Minutes} minutes ago",
                _ => $"{timeSinceAdded.Seconds} seconds ago"
            };
        }

        public static string FormatFileSize(long bytes)
        {
            if (bytes < 0) return string.Empty;
            return bytes switch
            {
                < 1024 => $"{bytes} B",
                < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
                < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
                _ => $"{bytes / (1024.0 * 1024 * 1024):0.#} GB"
            };
        }
    }
}