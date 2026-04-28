using System.Reflection;

namespace DumpLens.Tests.Unit.UI;

internal static class ViewModelAssemblyLoader
{
    public static Assembly Load()
    {
        var assemblyPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "DumpLens.App.ViewModels",
            "bin",
            "Debug",
            "net9.0",
            "DumpLens.App.ViewModels.dll"));

        Assert.True(File.Exists(assemblyPath), $"Expected view-model assembly at '{assemblyPath}'.");
        return Assembly.LoadFrom(assemblyPath);
    }
}
