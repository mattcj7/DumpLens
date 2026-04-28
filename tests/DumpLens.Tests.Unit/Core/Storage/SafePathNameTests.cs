using DumpLens.Core.Storage;

namespace DumpLens.Tests.Unit.Core.Storage;

public sealed class SafePathNameTests
{
    [Theory]
    [InlineData("Case 001", "Case 001")]
    [InlineData("  Case   001  ", "Case 001")]
    [InlineData("Case.Name (North)", "Case.Name (North)")]
    [InlineData("Case<>:\"/\\\\|?*Name", "Case-Name")]
    [InlineData("CON", "CON_")]
    [InlineData("NUL.txt", "NUL.txt_")]
    [InlineData("../Imports", "Imports")]
    [InlineData("..\\Imports", "Imports")]
    [InlineData("folder/../../evidence", "folder-evidence")]
    public void Create_SanitizesToASafeFileSystemSegment(string candidate, string expected)
    {
        var safeName = SafePathName.Create(candidate, nameof(candidate));

        Assert.Equal(expected, safeName.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../")]
    [InlineData("..\\")]
    public void Create_RejectsBlankOrUnsafeOnlyValues(string? candidate)
    {
        var exception = Assert.Throws<ArgumentException>(() => SafePathName.Create(candidate, nameof(candidate)));

        Assert.Equal(nameof(candidate), exception.ParamName);
    }

    [Fact]
    public void ResolvePathWithinRoot_BuildsAPathThatRemainsInsideTheRoot()
    {
        var rootPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DumpLens.SafePathNameTests", Guid.NewGuid().ToString("N")));

        var resolvedPath = SafePathName.ResolvePathWithinRoot(rootPath, "..\\Case Package");

        Assert.StartsWith(rootPath, resolvedPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith($"{Path.DirectorySeparatorChar}Case Package", resolvedPath, StringComparison.OrdinalIgnoreCase);
    }
}
