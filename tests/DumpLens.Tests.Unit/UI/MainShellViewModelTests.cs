using System.Collections;
using System.Reflection;

namespace DumpLens.Tests.Unit.UI;

public class MainShellViewModelTests
{
    private static readonly string[] RequiredLabels =
    [
        "Dashboard",
        "Sources",
        "Conversations",
        "Timeline",
        "Gaps & Deletions",
        "Entities & Aliases",
        "Leads",
        "AI Findings",
        "Reports",
        "Settings"
    ];

    [Fact]
    public void MainShellViewModel_Creates_All_Required_Navigation_Items()
    {
        var shellViewModel = CreateShellViewModel();
        var labels = GetNavigationLabels(shellViewModel);

        Assert.Equal(RequiredLabels.Length, labels.Count);
        Assert.Equal(RequiredLabels, labels);
    }

    [Fact]
    public void MainShellViewModel_Defaults_Selected_Item_To_Dashboard()
    {
        var shellViewModel = CreateShellViewModel();

        var selectedItem = GetPropertyValue(shellViewModel, "SelectedNavigationItem");
        var label = GetStringProperty(selectedItem, "Label");

        Assert.Equal("Dashboard", label);
    }

    [Fact]
    public void MainShellViewModel_Selecting_Sources_Updates_Current_Workspace()
    {
        var shellViewModel = CreateShellViewModel();
        var sourcesItem = GetNavigationItems(shellViewModel)
            .Single(item => GetStringProperty(item, "Label") == "Sources");

        SetPropertyValue(shellViewModel, "SelectedNavigationItem", sourcesItem);

        var workspace = GetPropertyValue(shellViewModel, "CurrentWorkspace");
        var title = GetStringProperty(workspace, "Title");
        var description = GetStringProperty(workspace, "Description");

        Assert.Equal("Sources", title);
        Assert.Contains("imported evidence sources", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MainShellViewModel_Contains_All_Required_Labels_Exactly_Once()
    {
        var shellViewModel = CreateShellViewModel();
        var labels = GetNavigationLabels(shellViewModel);

        foreach (var requiredLabel in RequiredLabels)
        {
            Assert.Equal(1, labels.Count(label => label == requiredLabel));
        }
    }

    private static object CreateShellViewModel()
    {
        var assembly = ViewModelAssemblyLoader.Load();
        var shellType = assembly.GetType("DumpLens.App.ViewModels.MainShellViewModel", throwOnError: true)!;
        return Activator.CreateInstance(shellType)!;
    }

    private static List<string> GetNavigationLabels(object shellViewModel)
    {
        return GetNavigationItems(shellViewModel)
            .Select(item => GetStringProperty(item, "Label"))
            .ToList();
    }

    private static List<object> GetNavigationItems(object shellViewModel)
    {
        var navigationItems = (IEnumerable)GetPropertyValue(shellViewModel, "NavigationItems");
        return navigationItems.Cast<object>().ToList();
    }

    private static object GetPropertyValue(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);

        var value = property!.GetValue(instance);
        Assert.NotNull(value);
        return value!;
    }

    private static string GetStringProperty(object instance, string propertyName)
    {
        var value = GetPropertyValue(instance, propertyName);
        var stringValue = Assert.IsType<string>(value);
        Assert.False(string.IsNullOrWhiteSpace(stringValue));
        return stringValue;
    }

    private static void SetPropertyValue(object instance, string propertyName, object value)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        property!.SetValue(instance, value);
    }
}
