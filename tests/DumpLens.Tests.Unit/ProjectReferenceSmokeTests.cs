namespace DumpLens.Tests.Unit;

public class ProjectReferenceSmokeTests
{
    [Fact]
    public void UnitTestProject_CanLoadExpectedAssemblies()
    {
        var assemblyNames = new[]
        {
            typeof(DumpLens.Core.AssemblyMarker).Assembly.GetName().Name,
            typeof(DumpLens.Application.AssemblyMarker).Assembly.GetName().Name,
            typeof(DumpLens.Normalization.AssemblyMarker).Assembly.GetName().Name,
            typeof(DumpLens.Reconciliation.AssemblyMarker).Assembly.GetName().Name,
            typeof(DumpLens.Analysis.AssemblyMarker).Assembly.GetName().Name,
            typeof(DumpLens.Security.AssemblyMarker).Assembly.GetName().Name,
            typeof(DumpLens.AI.AssemblyMarker).Assembly.GetName().Name
        };

        Assert.All(assemblyNames, name => Assert.False(string.IsNullOrWhiteSpace(name)));
    }
}
