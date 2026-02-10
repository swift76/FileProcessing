namespace CashPilot.FileProcessing
{
    /// <summary>
    /// To deserialize and use the application's settings
    /// </summary>
    internal class Settings
    {
        /// <summary>
        /// The value of input directory of files to be monitored
        /// </summary>
        public string? InputDirectory { get; init; }

        /// <summary>
        /// The value of archive directory, where successfully processed files are moved
        /// </summary>
        public string? ArchiveDirectory { get; init; }

        /// <summary>
        /// The value of error directory, where files with read errors are moved
        /// </summary>
        public string? ErrorDirectory { get; init; }

        /// <summary>
        /// The mask value of the file names being looked for
        /// </summary>
        public string? InputFileMask { get; init; }

        /// <summary>
        /// The value of directory, where log file is located
        /// </summary>
        public string? LogDirectory { get; init; }

        /// <summary>
        /// The value of delay in milliseconds between attempts to write to the log file
        /// </summary>
        public int? WriteLogDelayMilliseconds { get; init; }
    }
}
