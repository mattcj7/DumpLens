using System.Text.RegularExpressions;

namespace DumpLens.Tests.Unit.UI;

public sealed class MainWindowXamlBindingTests
{
    [Fact]
    public void MainWindow_Xaml_Uses_OneWay_For_Conversation_ReadOnly_Bindings()
    {
        var xaml = LoadNormalizedMainWindowXaml();

        Assert.Contains("{Binding ConversationMessageCountDisplay, Mode=OneWay}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding ConversationSourceCountDisplay, Mode=OneWay}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding ConversationGapCountDisplay, Mode=OneWay}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding ConversationPriorityScoreDisplay, Mode=OneWay}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding ConversationReconciliationStatus, Mode=OneWay}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding ConversationReviewStatus, Mode=OneWay}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding HasConversationSummary, Mode=OneWay, Converter={StaticResource BooleanToVisibilityConverter}}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding HasMessageContext, Mode=OneWay, Converter={StaticResource BooleanToVisibilityConverter}}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding HasSourceContext, Mode=OneWay, Converter={StaticResource BooleanToVisibilityConverter}}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding ConversationListEmptyStateMessage, Mode=OneWay}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding ThreadEmptyStateMessage, Mode=OneWay}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding SelectedMessageSenderDisplay, Mode=OneWay}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding RecipientDisplay, Mode=OneWay}", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_Xaml_Uses_OneWay_For_Source_And_Shell_ReadOnly_Bindings()
    {
        var xaml = LoadNormalizedMainWindowXaml();

        Assert.Contains("{Binding IsEmptyStateVisible, Mode=OneWay, Converter={StaticResource BooleanToVisibilityConverter}}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding EmptyStateMessage, Mode=OneWay}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding StoredFilePathDisplay, Mode=OneWay}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding RecordCountDisplay, Mode=OneWay}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding WarningCountDisplay, Mode=OneWay}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding WarningSummary.WarningCodeCounts, Mode=OneWay}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding Label, Mode=OneWay}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding Summary, Mode=OneWay}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding CurrentWorkspace.Title, Mode=OneWay}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding Inspector.Title, Mode=OneWay}", xaml, StringComparison.Ordinal);
    }

    private static string LoadNormalizedMainWindowXaml()
    {
        var path = Path.Combine(FindRepositoryRoot(), "src", "DumpLens.App", "MainWindow.xaml");
        var xaml = File.ReadAllText(path);
        return Regex.Replace(xaml, "\\s+", " ");
    }

    private static string FindRepositoryRoot()
    {
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDirectory is not null)
        {
            if (File.Exists(Path.Combine(currentDirectory.FullName, "DumpLens.sln")))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        throw new InvalidOperationException("Could not locate the DumpLens repository root.");
    }
}
