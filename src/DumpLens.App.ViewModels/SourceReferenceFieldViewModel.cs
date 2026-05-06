namespace DumpLens.App.ViewModels;

public sealed class SourceReferenceFieldViewModel
{
    public SourceReferenceFieldViewModel(string label, string value)
    {
        Label = string.IsNullOrWhiteSpace(label)
            ? throw new ArgumentException("A non-empty value is required.", nameof(label))
            : label.Trim();
        Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A non-empty value is required.", nameof(value))
            : value.Trim();
    }

    public string Label { get; }

    public string Value { get; }
}
