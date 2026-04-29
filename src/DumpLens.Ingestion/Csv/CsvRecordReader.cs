using System.Text;

namespace DumpLens.Ingestion.Csv;

internal sealed class CsvRecordReader : IDisposable
{
    private readonly StreamReader _reader;
    private readonly char _delimiter;
    private bool _disposed;

    public CsvRecordReader(string filePath, char delimiter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        _reader = new StreamReader(
            filePath,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: true);
        _delimiter = delimiter;
    }

    public int RecordNumber { get; private set; }

    public string[]? ReadRecord()
    {
        ThrowIfDisposed();

        var fields = new List<string>();
        var currentField = new StringBuilder();
        var inQuotes = false;
        var sawAnyContent = false;

        while (true)
        {
            var next = _reader.Read();
            if (next < 0)
            {
                if (!sawAnyContent && currentField.Length == 0 && fields.Count == 0)
                {
                    return null;
                }

                if (inQuotes)
                {
                    throw new FormatException("The CSV file contains an unterminated quoted field.");
                }

                fields.Add(currentField.ToString());
                RecordNumber++;
                return fields.ToArray();
            }

            var currentCharacter = (char)next;

            if (inQuotes)
            {
                if (currentCharacter == '"')
                {
                    if (_reader.Peek() == '"')
                    {
                        _reader.Read();
                        currentField.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    currentField.Append(currentCharacter);
                }

                sawAnyContent = true;
                continue;
            }

            if (currentCharacter == '"')
            {
                if (currentField.Length == 0)
                {
                    inQuotes = true;
                }
                else
                {
                    currentField.Append(currentCharacter);
                }

                sawAnyContent = true;
                continue;
            }

            if (currentCharacter == _delimiter)
            {
                fields.Add(currentField.ToString());
                currentField.Clear();
                sawAnyContent = true;
                continue;
            }

            if (currentCharacter == '\r')
            {
                if (_reader.Peek() == '\n')
                {
                    _reader.Read();
                }

                fields.Add(currentField.ToString());
                RecordNumber++;
                return fields.ToArray();
            }

            if (currentCharacter == '\n')
            {
                fields.Add(currentField.ToString());
                RecordNumber++;
                return fields.ToArray();
            }

            currentField.Append(currentCharacter);
            sawAnyContent = true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _reader.Dispose();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
