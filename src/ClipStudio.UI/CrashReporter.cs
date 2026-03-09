using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ClipStudio.UI;

/// <summary>
/// Detects, writes, and manages crash dump text files in the application data folder.
/// Crash dumps are plain-text files written synchronously on unhandled exceptions so that
/// the user can attach them to a bug report on the next session.
/// </summary>
public static class CrashReporter
{
    private static readonly string CrashDumpDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClipStudio");

    private const string CrashDumpPrefix = "crash_";
    private const string CrashDumpExtension = ".txt";

    /// <summary>
    /// Writes a crash dump file for the given unhandled exception.
    /// The filename encodes the UTC timestamp so multiple crashes do not overwrite each other.
    /// This method is intentionally synchronous and uses only primitive I/O so that it can
    /// run safely inside an <c>AppDomain.CurrentDomain.UnhandledException</c> handler.
    /// </summary>
    /// <param name="ex">The unhandled exception that caused the crash.</param>
    public static void WriteCrashDump(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(CrashDumpDir);

            var stamp    = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var filePath = Path.Combine(CrashDumpDir, $"{CrashDumpPrefix}{stamp}{CrashDumpExtension}");

            var sb = new StringBuilder();
            sb.AppendLine("=== ClipStudio Crash Report ===");
            sb.AppendLine($"Time    : {DateTime.UtcNow:u}");
            sb.AppendLine($"OS      : {RuntimeInformation.OSDescription}");
            sb.AppendLine($"Runtime : {RuntimeInformation.FrameworkDescription}");
            sb.AppendLine();
            sb.AppendLine("=== Exception ===");
            sb.AppendLine(ex.ToString());

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }
        catch
        {
            // If we cannot write the dump, there is nothing more we can do.
        }
    }

    /// <summary>
    /// Returns the paths of any crash dump files left over from previous sessions,
    /// ordered by oldest first so multiple crashes can be processed in sequence.
    /// </summary>
    /// <returns>An array of absolute file paths, possibly empty.</returns>
    public static string[] GetPendingCrashDumps()
    {
        try
        {
            return Directory.GetFiles(CrashDumpDir, $"{CrashDumpPrefix}*{CrashDumpExtension}")
                            .OrderBy(f => f)
                            .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Deletes the crash dump file at <paramref name="filePath"/>.
    /// Silently ignores errors if the file cannot be deleted.
    /// </summary>
    /// <param name="filePath">Absolute path to the crash dump file to delete.</param>
    public static void DeleteCrashDump(string filePath)
    {
        try { File.Delete(filePath); }
        catch { /* non-fatal */ }
    }

    /// <summary>
    /// Reads and returns the text content of a crash dump file, or an empty string on failure.
    /// </summary>
    /// <param name="filePath">Absolute path to the crash dump file to read.</param>
    /// <returns>The file content as a string.</returns>
    public static string ReadCrashDump(string filePath)
    {
        try { return File.ReadAllText(filePath, Encoding.UTF8); }
        catch { return string.Empty; }
    }
}
