using DumpLens.Application.Sources;

namespace DumpLens.App.ViewModels;

public sealed class SourceWarningSummaryViewModel
{
    public SourceWarningSummaryViewModel(SourceWarningSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        TotalWarnings = summary.TotalWarnings;
        WarningCodeCounts = summary.WarningCodeCounts
            .Take(5)
            .Select(static item => new SourceWarningCodeCountViewModel(item.WarningCode, item.Count))
            .ToArray();
    }

    public bool HasWarnings => TotalWarnings > 0;

    public bool HasWarningCodes => WarningCodeCounts.Count > 0;

    public int TotalWarnings { get; }

    public IReadOnlyList<SourceWarningCodeCountViewModel> WarningCodeCounts { get; }

    public sealed class SourceWarningCodeCountViewModel
    {
        public SourceWarningCodeCountViewModel(string warningCode, int count)
        {
            WarningCode = string.IsNullOrWhiteSpace(warningCode) ? "unknown_warning" : warningCode.Trim();
            Count = count;
        }

        public int Count { get; }

        public string WarningCode { get; }
    }
}
