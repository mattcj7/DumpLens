using System.Collections;
using System.ComponentModel;
using System.Globalization;
using DumpLens.Application.Cases;
using System.Windows.Input;

namespace DumpLens.App.ViewModels;

public sealed class CaseCreationViewModel : ObservableObject, INotifyDataErrorInfo
{
    private const string OpenedOperation = "case_creation_ui_opened";
    private const string SubmittedOperation = "case_creation_ui_submitted";
    private const string SucceededOperation = "case_creation_ui_succeeded";
    private const string FailedOperation = "case_creation_ui_failed";

    private readonly ICaseService _caseService;
    private readonly Action _onCancel;
    private readonly Action<CreateCaseResult> _onSuccess;
    private readonly Action<string, string, string, IReadOnlyDictionary<string, string>?> _logAction;
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);
    private readonly AsyncRelayCommand _submitCommand;
    private readonly RelayCommand _cancelCommand;
    private readonly string _correlationId;

    private string? _agency;
    private string? _caseNumber;
    private string? _generalErrorMessage;
    private string? _incidentEndText;
    private string? _incidentStartText;
    private string? _incidentType;
    private bool _isSubmitting;
    private string? _leadInvestigator;
    private string? _locationText;
    private string? _parentDirectoryPath;
    private string? _requestedPackageFolderName;
    private string? _statusMessage;
    private string? _summary;
    private string? _timezone;
    private string? _title;

    public CaseCreationViewModel(
        ICaseService caseService,
        Action<CreateCaseResult> onSuccess,
        Action onCancel,
        Action<string, string, string, IReadOnlyDictionary<string, string>?>? logAction = null,
        string? defaultTimezone = null)
    {
        _caseService = caseService ?? throw new ArgumentNullException(nameof(caseService));
        _onSuccess = onSuccess ?? throw new ArgumentNullException(nameof(onSuccess));
        _onCancel = onCancel ?? throw new ArgumentNullException(nameof(onCancel));
        _logAction = logAction ?? NoOpLog;
        _correlationId = Guid.NewGuid().ToString("N");
        _timezone = string.IsNullOrWhiteSpace(defaultTimezone)
            ? TimeZoneInfo.Local.Id
            : defaultTimezone.Trim();

        _submitCommand = new AsyncRelayCommand(SubmitAsync, () => !IsSubmitting);
        _cancelCommand = new RelayCommand(Cancel, () => !IsSubmitting);

        _logAction(
            OpenedOperation,
            _correlationId,
            "Case creation UI opened.",
            CreateSafeFieldSet());
    }

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public string? Agency
    {
        get => _agency;
        set => SetAndValidate(ref _agency, value, ValidateAgency, nameof(Agency));
    }

    public string? CaseNumber
    {
        get => _caseNumber;
        set => SetAndValidate(ref _caseNumber, value, ValidateCaseNumber, nameof(CaseNumber));
    }

    public ICommand CancelCommand => _cancelCommand;

    public string? GeneralErrorMessage
    {
        get => _generalErrorMessage;
        private set => SetProperty(ref _generalErrorMessage, value);
    }

    public bool HasErrors => _errors.Count > 0;

    public string? IncidentEndText
    {
        get => _incidentEndText;
        set => SetAndValidate(ref _incidentEndText, value, ValidateIncidentEndText, nameof(IncidentEndText), nameof(IncidentStartText), nameof(Timezone));
    }

    public string? IncidentStartText
    {
        get => _incidentStartText;
        set => SetAndValidate(ref _incidentStartText, value, ValidateIncidentStartText, nameof(IncidentStartText), nameof(IncidentEndText), nameof(Timezone));
    }

    public string? IncidentType
    {
        get => _incidentType;
        set => SetAndValidate(ref _incidentType, value, ValidateIncidentType, nameof(IncidentType));
    }

    public bool IsSubmitting
    {
        get => _isSubmitting;
        private set
        {
            if (!SetProperty(ref _isSubmitting, value))
            {
                return;
            }

            _submitCommand.RaiseCanExecuteChanged();
            _cancelCommand.RaiseCanExecuteChanged();
        }
    }

    public string? LeadInvestigator
    {
        get => _leadInvestigator;
        set => SetAndValidate(ref _leadInvestigator, value, ValidateLeadInvestigator, nameof(LeadInvestigator));
    }

    public string? LocationText
    {
        get => _locationText;
        set => SetAndValidate(ref _locationText, value, ValidateLocationText, nameof(LocationText));
    }

    public string? ParentDirectoryPath
    {
        get => _parentDirectoryPath;
        set => SetAndValidate(ref _parentDirectoryPath, value, ValidateParentDirectoryPath, nameof(ParentDirectoryPath));
    }

    public string? RequestedPackageFolderName
    {
        get => _requestedPackageFolderName;
        set => SetAndValidate(ref _requestedPackageFolderName, value, ValidateRequestedPackageFolderName, nameof(RequestedPackageFolderName));
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public ICommand SubmitCommand => _submitCommand;

    public string? Summary
    {
        get => _summary;
        set => SetAndValidate(ref _summary, value, ValidateSummary, nameof(Summary));
    }

    public string Timezone
    {
        get => _timezone ?? string.Empty;
        set => SetAndValidate(ref _timezone, value, ValidateTimezone, nameof(Timezone), nameof(IncidentStartText), nameof(IncidentEndText));
    }

    public string? Title
    {
        get => _title;
        set => SetAndValidate(ref _title, value, ValidateTitle, nameof(Title));
    }

    public IEnumerable GetErrors(string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return _errors.Values.SelectMany(static errors => errors).ToArray();
        }

        return _errors.TryGetValue(propertyName, out var errors)
            ? errors
            : Array.Empty<string>();
    }

    public async Task SubmitAsync()
    {
        GeneralErrorMessage = null;
        StatusMessage = null;
        ValidateAll();

        if (HasErrors)
        {
            GeneralErrorMessage = "Fix the highlighted fields and try again.";
            return;
        }

        IsSubmitting = true;
        StatusMessage = "Creating case package...";

        var request = BuildRequest();
        _logAction(
            SubmittedOperation,
            _correlationId,
            "Case creation submitted.",
            CreateSafeFieldSet());

        try
        {
            var result = await _caseService.CreateAsync(request);

            _logAction(
                SucceededOperation,
                _correlationId,
                "Case creation succeeded.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["case_id"] = result.CaseId,
                    ["package_id"] = result.PackageId,
                    ["case_number_present"] = (!string.IsNullOrWhiteSpace(result.CaseNumber)).ToString(CultureInfo.InvariantCulture)
                });

            StatusMessage = null;
            _onSuccess(result);
        }
        catch (Exception exception)
        {
            ApplySafeFailure(exception);
            _logAction(
                FailedOperation,
                _correlationId,
                "Case creation failed.",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["failure_type"] = exception.GetType().Name,
                    ["case_number_present"] = (!string.IsNullOrWhiteSpace(CaseNumber)).ToString(CultureInfo.InvariantCulture),
                    ["title_present"] = (!string.IsNullOrWhiteSpace(Title)).ToString(CultureInfo.InvariantCulture)
                });
        }
        finally
        {
            IsSubmitting = false;
            StatusMessage = null;
        }
    }

    private static void NoOpLog(
        string operation,
        string correlationId,
        string message,
        IReadOnlyDictionary<string, string>? fields)
    {
    }

    private void Cancel()
    {
        if (IsSubmitting)
        {
            return;
        }

        _onCancel();
    }

    private void ValidateAll()
    {
        ValidateProperty(nameof(CaseNumber));
        ValidateProperty(nameof(Title));
        ValidateProperty(nameof(IncidentType));
        ValidateProperty(nameof(IncidentStartText));
        ValidateProperty(nameof(IncidentEndText));
        ValidateProperty(nameof(Timezone));
        ValidateProperty(nameof(LocationText));
        ValidateProperty(nameof(LeadInvestigator));
        ValidateProperty(nameof(Agency));
        ValidateProperty(nameof(Summary));
        ValidateProperty(nameof(ParentDirectoryPath));
        ValidateProperty(nameof(RequestedPackageFolderName));
    }

    private CreateCaseRequest BuildRequest()
    {
        var timezone = ResolveTimezone(trimmedOnly: true);
        var incidentStartUtc = ParseIncidentDateTime(IncidentStartText, timezone);
        var incidentEndUtc = ParseIncidentDateTime(IncidentEndText, timezone);

        return new CreateCaseRequest
        {
            CaseNumber = NormalizeOptional(CaseNumber),
            Title = NormalizeOptional(Title),
            IncidentType = NormalizeOptional(IncidentType),
            IncidentStartUtc = incidentStartUtc,
            IncidentEndUtc = incidentEndUtc,
            IncidentTimezone = NormalizeOptional(Timezone),
            IncidentLocationText = NormalizeOptional(LocationText),
            LeadInvestigator = NormalizeOptional(LeadInvestigator),
            Agency = NormalizeOptional(Agency),
            Summary = NormalizeOptional(Summary),
            ParentDirectoryPath = NormalizeOptional(ParentDirectoryPath),
            RequestedPackageFolderName = NormalizeOptional(RequestedPackageFolderName),
            CorrelationId = _correlationId
        };
    }

    private IReadOnlyDictionary<string, string> CreateSafeFieldSet()
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["case_number_present"] = (!string.IsNullOrWhiteSpace(CaseNumber)).ToString(CultureInfo.InvariantCulture),
            ["title_present"] = (!string.IsNullOrWhiteSpace(Title)).ToString(CultureInfo.InvariantCulture),
            ["incident_start_present"] = (!string.IsNullOrWhiteSpace(IncidentStartText)).ToString(CultureInfo.InvariantCulture),
            ["incident_end_present"] = (!string.IsNullOrWhiteSpace(IncidentEndText)).ToString(CultureInfo.InvariantCulture),
            ["timezone_present"] = (!string.IsNullOrWhiteSpace(Timezone)).ToString(CultureInfo.InvariantCulture),
            ["parent_directory_present"] = (!string.IsNullOrWhiteSpace(ParentDirectoryPath)).ToString(CultureInfo.InvariantCulture),
            ["requested_package_folder_present"] = (!string.IsNullOrWhiteSpace(RequestedPackageFolderName)).ToString(CultureInfo.InvariantCulture)
        };
    }

    private void ApplySafeFailure(Exception exception)
    {
        StatusMessage = null;

        switch (exception)
        {
            case DirectoryNotFoundException:
                SetSingleError(nameof(ParentDirectoryPath), "Enter an existing parent/root directory.");
                GeneralErrorMessage = "Case could not be created because the parent/root directory was not found.";
                return;

            case UnauthorizedAccessException:
                GeneralErrorMessage = "Case could not be created in that folder. Choose a different folder or check folder permissions.";
                return;

            case ArgumentException argumentException when string.Equals(argumentException.ParamName, nameof(CreateCaseRequest.Title), StringComparison.Ordinal):
                SetSingleError(nameof(Title), "Title is required.");
                GeneralErrorMessage = "Enter a title and try again.";
                return;

            case ArgumentException argumentException when string.Equals(argumentException.ParamName, nameof(CreateCaseRequest.ParentDirectoryPath), StringComparison.Ordinal):
                SetSingleError(nameof(ParentDirectoryPath), "Enter an existing parent/root directory.");
                GeneralErrorMessage = "Enter a valid parent/root directory and try again.";
                return;

            case ArgumentException argumentException when string.Equals(argumentException.ParamName, nameof(CreateCaseRequest.IncidentEndUtc), StringComparison.Ordinal):
                SetSingleError(nameof(IncidentEndText), "Incident end cannot be before incident start.");
                GeneralErrorMessage = "Fix the incident date/time fields and try again.";
                return;

            default:
                GeneralErrorMessage = "Case could not be created. Review the form and try again.";
                return;
        }
    }

    private void SetAndValidate(ref string? field, string? value, Action validate, string propertyName, params string[] additionalPropertiesToValidate)
    {
        if (!SetProperty(ref field, value))
        {
            return;
        }

        GeneralErrorMessage = null;
        validate();

        foreach (var additionalProperty in additionalPropertiesToValidate)
        {
            ValidateProperty(additionalProperty);
        }
    }

    private void ValidateProperty(string propertyName)
    {
        switch (propertyName)
        {
            case nameof(CaseNumber):
                ValidateCaseNumber();
                break;
            case nameof(Title):
                ValidateTitle();
                break;
            case nameof(IncidentType):
                ValidateIncidentType();
                break;
            case nameof(IncidentStartText):
                ValidateIncidentStartText();
                break;
            case nameof(IncidentEndText):
                ValidateIncidentEndText();
                break;
            case nameof(Timezone):
                ValidateTimezone();
                break;
            case nameof(LocationText):
                ValidateLocationText();
                break;
            case nameof(LeadInvestigator):
                ValidateLeadInvestigator();
                break;
            case nameof(Agency):
                ValidateAgency();
                break;
            case nameof(Summary):
                ValidateSummary();
                break;
            case nameof(ParentDirectoryPath):
                ValidateParentDirectoryPath();
                break;
            case nameof(RequestedPackageFolderName):
                ValidateRequestedPackageFolderName();
                break;
        }
    }

    private void ValidateAgency()
    {
        ClearErrors(nameof(Agency));
    }

    private void ValidateCaseNumber()
    {
        ClearErrors(nameof(CaseNumber));
    }

    private void ValidateIncidentEndText()
    {
        ClearErrors(nameof(IncidentEndText));

        if (string.IsNullOrWhiteSpace(IncidentEndText))
        {
            ValidateIncidentRange();
            return;
        }

        if (!TryParseIncidentDateTime(IncidentEndText, ResolveTimezone(), out _, out var errorMessage))
        {
            AddError(nameof(IncidentEndText), errorMessage!);
            return;
        }

        ValidateIncidentRange();
    }

    private void ValidateIncidentRange()
    {
        if (!TryParseIncidentDateTime(IncidentStartText, ResolveTimezone(), out var incidentStartUtc, out _) ||
            !TryParseIncidentDateTime(IncidentEndText, ResolveTimezone(), out var incidentEndUtc, out _))
        {
            return;
        }

        if (incidentStartUtc.HasValue &&
            incidentEndUtc.HasValue &&
            incidentEndUtc.Value < incidentStartUtc.Value)
        {
            AddError(nameof(IncidentEndText), "Incident end cannot be before incident start.");
        }
    }

    private void ValidateIncidentStartText()
    {
        ClearErrors(nameof(IncidentStartText));

        if (string.IsNullOrWhiteSpace(IncidentStartText))
        {
            return;
        }

        if (!TryParseIncidentDateTime(IncidentStartText, ResolveTimezone(), out _, out var errorMessage))
        {
            AddError(nameof(IncidentStartText), errorMessage!);
        }
    }

    private void ValidateIncidentType()
    {
        ClearErrors(nameof(IncidentType));
    }

    private void ValidateLeadInvestigator()
    {
        ClearErrors(nameof(LeadInvestigator));
    }

    private void ValidateLocationText()
    {
        ClearErrors(nameof(LocationText));
    }

    private void ValidateParentDirectoryPath()
    {
        ClearErrors(nameof(ParentDirectoryPath));

        var normalized = NormalizeOptional(ParentDirectoryPath);
        if (normalized is null)
        {
            AddError(nameof(ParentDirectoryPath), "Parent/root directory is required.");
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(normalized);
            if (!Path.IsPathRooted(fullPath) || !Directory.Exists(fullPath))
            {
                AddError(nameof(ParentDirectoryPath), "Enter an existing parent/root directory.");
            }
        }
        catch
        {
            AddError(nameof(ParentDirectoryPath), "Enter an existing parent/root directory.");
        }
    }

    private void ValidateRequestedPackageFolderName()
    {
        ClearErrors(nameof(RequestedPackageFolderName));

        var normalized = NormalizeOptional(RequestedPackageFolderName);
        if (normalized is null)
        {
            return;
        }

        if (normalized.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            normalized.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal) ||
            normalized.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries).Any(static segment => string.Equals(segment, "..", StringComparison.Ordinal)))
        {
            AddError(nameof(RequestedPackageFolderName), "Package folder name must be a folder name, not a path.");
        }
    }

    private void ValidateSummary()
    {
        ClearErrors(nameof(Summary));
    }

    private void ValidateTimezone()
    {
        ClearErrors(nameof(Timezone));

        var normalized = NormalizeOptional(Timezone);
        if (normalized is null)
        {
            return;
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(normalized);
        }
        catch
        {
            AddError(nameof(Timezone), "Enter a valid Windows timezone ID.");
        }
    }

    private void ValidateTitle()
    {
        ClearErrors(nameof(Title));

        if (NormalizeOptional(Title) is null)
        {
            AddError(nameof(Title), "Title is required.");
        }
    }

    private void AddError(string propertyName, string message)
    {
        if (!_errors.TryGetValue(propertyName, out var propertyErrors))
        {
            propertyErrors = [];
            _errors[propertyName] = propertyErrors;
        }

        if (propertyErrors.Contains(message, StringComparer.Ordinal))
        {
            return;
        }

        propertyErrors.Add(message);
        RaiseErrorsChanged(propertyName);
        OnPropertyChanged(nameof(HasErrors));
    }

    private void ClearErrors(string propertyName)
    {
        if (!_errors.Remove(propertyName))
        {
            return;
        }

        RaiseErrorsChanged(propertyName);
        OnPropertyChanged(nameof(HasErrors));
    }

    private void RaiseErrorsChanged(string propertyName)
    {
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
    }

    private void SetSingleError(string propertyName, string message)
    {
        ClearErrors(propertyName);
        AddError(propertyName, message);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private TimeZoneInfo? ResolveTimezone(bool trimmedOnly = false)
    {
        var normalized = NormalizeOptional(Timezone);
        if (normalized is null)
        {
            return trimmedOnly ? null : TimeZoneInfo.Local;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(normalized);
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset? ParseIncidentDateTime(string? value, TimeZoneInfo? timezone)
    {
        if (!TryParseIncidentDateTime(value, timezone, out var dateTimeOffset, out _))
        {
            return null;
        }

        return dateTimeOffset;
    }

    private static bool TryParseIncidentDateTime(
        string? value,
        TimeZoneInfo? timezone,
        out DateTimeOffset? utcValue,
        out string? errorMessage)
    {
        utcValue = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!DateTime.TryParse(
                value.Trim(),
                CultureInfo.CurrentCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var localValue))
        {
            errorMessage = "Enter a valid date/time, for example 2026-04-28 13:45.";
            return false;
        }

        var effectiveTimezone = timezone ?? TimeZoneInfo.Local;
        var unspecifiedLocal = DateTime.SpecifyKind(localValue, DateTimeKind.Unspecified);

        try
        {
            utcValue = TimeZoneInfo.ConvertTimeToUtc(unspecifiedLocal, effectiveTimezone);
            return true;
        }
        catch
        {
            errorMessage = "Enter a date/time that is valid for the selected timezone.";
            return false;
        }
    }
}
