using DumpLens.Application.Imports;

namespace DumpLens.Application.CallImports;

public static class CallImportFieldNames
{
    public const string Timestamp = ImportFieldNames.Timestamp;
    public const string Caller = ImportFieldNames.Caller;
    public const string Callee = ImportFieldNames.Callee;
    public const string SenderAlias = ImportFieldNames.Sender;
    public const string RecipientAlias = ImportFieldNames.Recipient;
    public const string Direction = ImportFieldNames.Direction;
    public const string Duration = ImportFieldNames.Duration;
    public const string CallType = ImportFieldNames.CallType;
    public const string PlatformOrCarrier = "platform_or_carrier";
    public const string PlatformAlias = ImportFieldNames.Platform;
}
