namespace DumpLens.Tests.Integration;

public class ProjectReferenceSmokeTests
{
    [Fact]
    public void IntegrationTestProject_CanLoadExpectedAssemblies()
    {
        var assemblyNames = new[]
        {
            typeof(DumpLens.Core.AssemblyMarker).Assembly.GetName().Name,
            typeof(DumpLens.Application.AssemblyMarker).Assembly.GetName().Name,
            typeof(DumpLens.Persistence.AssemblyMarker).Assembly.GetName().Name,
            typeof(DumpLens.Ingestion.AssemblyMarker).Assembly.GetName().Name,
            typeof(DumpLens.Search.AssemblyMarker).Assembly.GetName().Name,
            typeof(DumpLens.Reporting.AssemblyMarker).Assembly.GetName().Name,
            typeof(DumpLens.Audit.AssemblyMarker).Assembly.GetName().Name
        };

        Assert.All(assemblyNames, name => Assert.False(string.IsNullOrWhiteSpace(name)));
    }
}
