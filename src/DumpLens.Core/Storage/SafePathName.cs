namespace DumpLens.Core.Storage;

public sealed record SafePathName
{
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON",
        "PRN",
        "AUX",
        "NUL",
        "COM1",
        "COM2",
        "COM3",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9"
    };

    private SafePathName(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static SafePathName Create(string? candidate, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);

        if (string.IsNullOrWhiteSpace(candidate))
        {
            throw new ArgumentException("A non-empty file-system name is required.", parameterName);
        }

        var invalidCharacters = Path.GetInvalidFileNameChars();
        var buffer = new char[candidate.Length];
        var length = 0;

        foreach (var character in candidate.Trim())
        {
            buffer[length++] = ShouldReplace(character, invalidCharacters) ? '-' : character;
        }

        var sanitized = new string(buffer, 0, length);

        while (sanitized.Contains("..", StringComparison.Ordinal))
        {
            sanitized = sanitized.Replace("..", "-", StringComparison.Ordinal);
        }

        sanitized = CollapseRepeatedCharacters(sanitized, '-');
        sanitized = CollapseWhitespace(sanitized).Trim(' ', '.');

        if (sanitized.Length == 0)
        {
            throw new ArgumentException("The provided value cannot be converted into a safe file-system name.", parameterName);
        }

        var reservedCheckName = Path.GetFileNameWithoutExtension(sanitized);
        if (ReservedDeviceNames.Contains(reservedCheckName))
        {
            sanitized += "_";
        }

        return new SafePathName(sanitized);
    }

    public static string ResolvePathWithinRoot(string rootPath, params string[] candidateSegments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(candidateSegments);

        var fullRootPath = Path.GetFullPath(rootPath);
        if (!Path.IsPathRooted(fullRootPath))
        {
            throw new ArgumentException("The root path must be absolute.", nameof(rootPath));
        }

        var path = fullRootPath;
        foreach (var segment in candidateSegments)
        {
            path = Path.Combine(path, Create(segment, nameof(candidateSegments)).Value);
        }

        var fullCandidatePath = Path.GetFullPath(path);
        var comparisonRoot = EnsureTrailingDirectorySeparator(fullRootPath);

        if (!fullCandidatePath.StartsWith(comparisonRoot, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fullCandidatePath, fullRootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The resolved path must remain within the configured root.");
        }

        return fullCandidatePath;
    }

    public override string ToString() => Value;

    private static bool ShouldReplace(char character, IReadOnlyCollection<char> invalidCharacters)
    {
        return invalidCharacters.Contains(character)
            || character == Path.DirectorySeparatorChar
            || character == Path.AltDirectorySeparatorChar
            || char.IsControl(character);
    }

    private static string CollapseRepeatedCharacters(string value, char target)
    {
        if (value.Length <= 1)
        {
            return value;
        }

        var result = new char[value.Length];
        var length = 0;
        var previousWasTarget = false;

        foreach (var character in value)
        {
            var currentIsTarget = character == target;
            if (currentIsTarget && previousWasTarget)
            {
                continue;
            }

            result[length++] = character;
            previousWasTarget = currentIsTarget;
        }

        return new string(result, 0, length).Trim(target);
    }

    private static string CollapseWhitespace(string value)
    {
        if (value.Length <= 1)
        {
            return value;
        }

        var result = new char[value.Length];
        var length = 0;
        var previousWasWhitespace = false;

        foreach (var character in value)
        {
            var currentIsWhitespace = char.IsWhiteSpace(character);
            if (currentIsWhitespace)
            {
                if (previousWasWhitespace)
                {
                    continue;
                }

                result[length++] = ' ';
                previousWasWhitespace = true;
                continue;
            }

            result[length++] = character;
            previousWasWhitespace = false;
        }

        return new string(result, 0, length);
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
