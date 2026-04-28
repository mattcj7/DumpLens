using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace DumpLens.Persistence.Database;

public sealed class MigrationScript
{
    public MigrationScript(string version, string name, string sql, string? sourceName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        if (!version.All(char.IsDigit))
        {
            throw new ArgumentException("Migration versions must be numeric.", nameof(version));
        }

        Version = version;
        Name = name;
        Sql = sql;
        SourceName = string.IsNullOrWhiteSpace(sourceName)
            ? $"{version}_{name}.sql"
            : sourceName;
        VersionNumber = long.Parse(version, CultureInfo.InvariantCulture);
        Checksum = ComputeChecksum(sql);
    }

    public string Version { get; }

    public string Name { get; }

    public string SourceName { get; }

    public string Sql { get; }

    public string Checksum { get; }

    internal long VersionNumber { get; }

    public static MigrationScript FromEmbeddedResource(Assembly assembly, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        var assemblyName = assembly.GetName().Name
            ?? throw new InvalidOperationException("Migration assembly name is unavailable.");
        var resourcePrefix = $"{assemblyName}.Migrations.";

        if (!resourceName.StartsWith(resourcePrefix, StringComparison.Ordinal) ||
            !resourceName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Resource name is not a supported migration script.", nameof(resourceName));
        }

        var fileName = resourceName[resourcePrefix.Length..];
        var baseName = fileName[..^4];
        var separatorIndex = baseName.IndexOf('_');

        if (separatorIndex <= 0 || separatorIndex == baseName.Length - 1)
        {
            throw new InvalidOperationException(
                $"Migration resource '{resourceName}' must follow the '{{version}}_{{name}}.sql' naming convention.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Migration resource '{resourceName}' could not be loaded.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        return new MigrationScript(
            baseName[..separatorIndex],
            baseName[(separatorIndex + 1)..],
            reader.ReadToEnd(),
            fileName);
    }

    public static string ComputeChecksum(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        var bytes = Encoding.UTF8.GetBytes(sql);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
