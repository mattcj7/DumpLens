using System.IO;
using System.Text;

namespace DumpLens.App;

internal sealed class ShellSessionLogger : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly string _logFilePath;

    public ShellSessionLogger()
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DumpLens",
            "Logs");

        Directory.CreateDirectory(logDirectory);
        _logFilePath = Path.Combine(logDirectory, "app-shell.log");
    }

    public void LogInformation(
        string operation,
        string correlationId,
        string message,
        IReadOnlyDictionary<string, string>? fields = null)
    {
        WriteLogLine("Information", operation, correlationId, message, fields);
    }

    public void Dispose()
    {
    }

    private void WriteLogLine(
        string level,
        string operation,
        string correlationId,
        string message,
        IReadOnlyDictionary<string, string>? fields)
    {
        var builder = new StringBuilder();
        builder.Append(DateTimeOffset.UtcNow.ToString("O"));
        builder.Append(" level=").Append(level);
        builder.Append(" operation=").Append(Sanitize(operation));
        builder.Append(" correlation_id=").Append(Sanitize(correlationId));
        builder.Append(" message=\"").Append(Sanitize(message)).Append('"');

        if (fields is not null)
        {
            foreach (var pair in fields)
            {
                builder.Append(' ')
                    .Append(Sanitize(pair.Key))
                    .Append('=')
                    .Append(Sanitize(pair.Value));
            }
        }

        try
        {
            lock (_syncRoot)
            {
                File.AppendAllText(_logFilePath, builder.AppendLine().ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            System.Diagnostics.Debug.WriteLine(builder.ToString());
        }
    }

    private static string Sanitize(string value)
    {
        return value.Replace("\r", " ").Replace("\n", " ").Replace("\"", "'");
    }
}
