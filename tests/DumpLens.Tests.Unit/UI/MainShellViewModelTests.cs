using System.Collections;
using System.Reflection;
using System.Windows.Input;

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
        Assert.Contains("registered sources", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MainShellViewModel_Selecting_Conversations_Updates_Current_Workspace()
    {
        var shellViewModel = CreateShellViewModel();
        var conversationsItem = GetNavigationItems(shellViewModel)
            .Single(item => GetStringProperty(item, "Label") == "Conversations");

        SetPropertyValue(shellViewModel, "SelectedNavigationItem", conversationsItem);

        var workspace = GetPropertyValue(shellViewModel, "CurrentWorkspace");
        var title = GetStringProperty(workspace, "Title");
        var description = GetStringProperty(workspace, "Description");

        Assert.Equal("Conversations", title);
        Assert.Contains("message threads", description, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public void MainShellViewModel_Opening_And_Canceling_Import_Wizard_Resets_Shell_State()
    {
        var shellViewModel = CreateShellViewModel();
        var openImportCommand = Assert.IsAssignableFrom<ICommand>(GetPropertyValue(shellViewModel, "OpenImportWizardCommand"));

        openImportCommand.Execute(null);

        Assert.True(GetBooleanProperty(shellViewModel, "IsImportWizardOpen"));
        var importWizard = GetPropertyValue(shellViewModel, "ImportWizard");
        var cancelCommand = Assert.IsAssignableFrom<ICommand>(GetPropertyValue(importWizard, "CancelCommand"));

        cancelCommand.Execute(null);

        Assert.False(GetBooleanProperty(shellViewModel, "IsImportWizardOpen"));
        Assert.Null(GetNullablePropertyValue(shellViewModel, "ImportWizard"));
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

    private static bool GetBooleanProperty(object instance, string propertyName)
    {
        var value = GetPropertyValue(instance, propertyName);
        return Assert.IsType<bool>(value);
    }

    private static object? GetNullablePropertyValue(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        return property!.GetValue(instance);
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
