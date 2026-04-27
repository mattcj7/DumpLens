namespace DumpLens.Tests.Performance;

public class ProjectReferenceSmokeTests
{
    [Fact]
    public void PerformanceTestProject_CanLoadExpectedAssemblies()
    {
        var assemblyNames = new[]
        {
            typeof(DumpLens.Core.AssemblyMarker).Assembly.GetName().Name,
            typeof(DumpLens.Application.AssemblyMarker).Assembly.GetName().Name,
            typeof(DumpLens.Persistence.AssemblyMarker).Assembly.GetName().Name,
            typeof(DumpLens.Search.AssemblyMarker).Assembly.GetName().Name,
            typeof(DumpLens.Reconciliation.AssemblyMarker).Assembly.GetName().Name
        };

        Assert.All(assemblyNames, name => Assert.False(string.IsNullOrWhiteSpace(name)));
    }
}
