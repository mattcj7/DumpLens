using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DumpLens.Core.Audit;

public static class AuditEventCanonicalizer
{
    public static string CreateCanonicalJson(AuditEventHashInput input)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(
            buffer,
            new JsonWriterOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

        writer.WriteStartObject();
        writer.WriteString("id", input.Id);
        WriteOptionalString(writer, "case_id", input.CaseId);
        WriteOptionalString(writer, "user_id", input.UserId);
        writer.WriteString("action_type", input.ActionType);
        WriteOptionalString(writer, "entity_type", input.EntityType);
        WriteOptionalString(writer, "entity_id", input.EntityId);
        writer.WriteString("summary", input.Summary);
        WriteJsonField(writer, "old_value_json", input.OldValueJson);
        WriteJsonField(writer, "new_value_json", input.NewValueJson);
        WriteOptionalString(writer, "reason", input.Reason);
        writer.WriteString("event_time_utc", input.EventTimeUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        WriteOptionalString(writer, "workstation", input.Workstation);
        WriteOptionalString(writer, "app_version", input.AppVersion);
        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteOptionalString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        writer.WritePropertyName(propertyName);
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value);
    }

    private static void WriteJsonField(Utf8JsonWriter writer, string propertyName, string? json)
    {
        writer.WritePropertyName(propertyName);
        if (json is null)
        {
            writer.WriteNullValue();
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            WriteCanonicalElement(writer, document.RootElement);
        }
        catch (JsonException)
        {
            writer.WriteStringValue(json);
        }
    }

    private static void WriteCanonicalElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalElement(writer, property.Value);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalElement(writer, item);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                break;

            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;

            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;

            default:
                throw new InvalidOperationException($"Unsupported JSON value kind '{element.ValueKind}'.");
        }
    }
}
