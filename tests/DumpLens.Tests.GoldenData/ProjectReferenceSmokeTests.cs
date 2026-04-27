namespace DumpLens.Tests.GoldenData;

public class ProjectReferenceSmokeTests
{
    [Fact]
    public void GoldenDataTestProject_CanLoadExpectedAssemblies()
    {
        var assemblyNames = new[]
        {
            typeof(DumpLens.Core.AssemblyMarker).Assembly.GetName().Name,
            typeof(DumpLens.Ingestion.AssemblyMarker).Assembly.GetName().Name,
            typeof(DumpLens.Normalization.AssemblyMarker).Assembly.GetName().Name,
            typeof(DumpLens.Reconciliation.AssemblyMarker).Assembly.GetName().Name,
            typeof(DumpLens.Analysis.AssemblyMarker).Assembly.GetName().Name
        };

        Assert.All(assemblyNames, name => Assert.False(string.IsNullOrWhiteSpace(name)));
    }
}
