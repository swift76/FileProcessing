using CashPilot.FileProcessing;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using System.Globalization;

internal class Program
{
    private static BlockingCollection<string> queue = new(int.MaxValue);
    
    private static Settings? appSettings;

    static async Task Main()
    {
        try
        {
            //Getting configuration settings values from appsettings.json
            var configBuilder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            appSettings = configBuilder.GetSection("Settings").Get<Settings>() ?? throw new ApplicationException("Configuration is missing");

            //Checking the values of the settings
            CheckDirectorySetting(appSettings.InputDirectory, "Input directory", false);
            
            CheckDirectorySetting(appSettings.ArchiveDirectory, "Archive directory", true);
            
            CheckDirectorySetting(appSettings.ErrorDirectory, "Error directory", true);
            
            if (string.IsNullOrWhiteSpace(appSettings.InputFileMask))
            {
                throw new ApplicationException($"File mask is not specified");
            }

            CheckDirectorySetting(appSettings.LogDirectory, "Log directory", true);

            if (!appSettings.WriteLogDelayMilliseconds.HasValue)
            {
                throw new ApplicationException($"Delay in milliseconds between attempts to write to the log file is not specified");
            }

            // Ctrl+C
            var cts = new CancellationTokenSource();
            var token = cts.Token;
            Console.CancelKeyPress += (s, e) =>
            {
                WriteConsoleLine("Shutting down by user", false);
                e.Cancel = true;
                cts.Cancel();
            };

            // Enter key
            _ = Task.Run(() =>
            {
                Console.ReadLine();
                WriteConsoleLine("Shutting down by user", false);
                cts.Cancel();
            });

            //Adding already existing files to the queue
            foreach (var file in Directory.GetFiles(appSettings.InputDirectory, appSettings.InputFileMask))
            {
                queue.TryAdd(file);
            }

            //Setting up file watcher for monitoring file changes
            using var watcher = new FileSystemWatcher(appSettings.InputDirectory)
            {
                Filter = appSettings.InputFileMask,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
            };

            watcher.Created += (_, e) => queue.TryAdd(e.FullPath);
            watcher.Changed += (_, e) => queue.TryAdd(e.FullPath);
            watcher.EnableRaisingEvents = true;

            _ = Task.Run(() => WorkerAsync(token));

            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (OperationCanceledException)
            {
            }

            // Shutdown
            watcher.EnableRaisingEvents = false;
            queue.CompleteAdding();
        }
        catch (Exception ex)
        {
            WriteConsoleLine(ex.ToString(), true);
        }
    }

    /// <summary>
    /// To run the worker for the processing
    /// </summary>
    /// <param name="token">Cancellation token</param>
    /// <returns>Whether the read operation was successful or not</returns>
    private static async Task WorkerAsync(CancellationToken token)
    {
        try
        {
            foreach (var path in queue.GetConsumingEnumerable(token))
            {
                try
                {
                    if (await ProcessFileAsync(path, token))
                    {
                        MoveFile(path, appSettings.ArchiveDirectory);
                    }
                }
                catch (Exception ex)
                {
                    WriteConsoleLine(ex.ToString(), true);

                    try
                    {
                        MoveFile(path, appSettings.ErrorDirectory);
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Processing a file
    /// </summary>
    /// <param name="path">File's full path</param>
    /// <param name="token">Cancellation token</param>
    /// <returns></returns>
    private static async Task<bool> ProcessFileAsync(string path, CancellationToken token)
    {
        string fileContents;

        try
        {
            fileContents = await File.ReadAllTextAsync(path, token);
        }
        catch
        {
            return false;
        }

        var fileLines = fileContents.Split([ "\r\n", "\r", "\n" ], StringSplitOptions.RemoveEmptyEntries);
        double total = 0;
        foreach (var line in fileLines)
        {
            var lineWords = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in lineWords)
            {
                if (double.TryParse(word, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.CurrentCulture, out var valueCurrent))
                {
                    total += valueCurrent;
                }
            }
        }

        var fileStatistics = $"Processed: {path}, lines: {fileLines.Length}, total: {total}";
        WriteConsoleLine(fileStatistics, false);
        await WriteLogFile(fileStatistics, token);

        return true;
    }

    /// <summary>
    /// To check the value of the directory setting
    /// </summary>
    /// <param name="value">Value of the directory setting</param>
    /// <param name="caption">Caption of the setting</param>
    /// <param name="createIfMissing">Indicates whether the directory should be created if missing, or otherwise an exception should be thrown</param>
    /// <exception cref="ApplicationException"></exception>
    private static void CheckDirectorySetting(string? value, string caption, bool createIfMissing)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ApplicationException($"{caption} is not specified");
        }

        if (!Directory.Exists(value))
        {
            if (createIfMissing)
            {
                Directory.CreateDirectory(value);
            }
            else
            {
                throw new ApplicationException($"{caption} doesn't exist");
            }
        }
    }

    /// <summary>
    /// To write an information to the console
    /// </summary>
    /// <param name="message">Message text</param>
    /// <param name="isError">Specifies whether the message is an error or just an information</param>
    private static void WriteConsoleLine(string message, bool isError)
    {
        var messageType = isError ? "ERROR" : "INFO";
        Console.WriteLine($"{DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")} {messageType}: {message}");
    }

    /// <summary>
    /// To move file from input directory to either archive or error directory
    /// </summary>
    /// <param name="originalFile">Processed file</param>
    /// <param name="moveDirectory">Destination directory</param>
    private static void MoveFile(string originalFile, string moveDirectory)
    {
        var newFile = $"{DateTime.Now.ToString("yyyyMMddHHmmss")}_{Guid.NewGuid().ToString().Replace("-", string.Empty)}_{Path.GetFileName(originalFile)}";
        File.Move(originalFile, Path.Combine(moveDirectory, newFile));
    }

    private static async Task WriteLogFile(string fileStatistics, CancellationToken token)
    {
        var logFile = Path.Combine(appSettings.LogDirectory, $"{DateTime.Now.ToString("yyyyMMddHHmmss")}.log");

        while (true)
        {
            try
            {
                File.AppendAllText(logFile, fileStatistics);
                return;
            }
            catch
            {
                await Task.Delay(appSettings.WriteLogDelayMilliseconds.Value, token);
            }
        }
    }
}