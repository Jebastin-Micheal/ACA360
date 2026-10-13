using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ACA360.Core.Binders
{
    /// <summary>
    /// Reads a JSON string exactly as sent. Program.cs registers UppercaseStringJsonConverter for
    /// every string in every JSON request; a property marked
    /// <c>[JsonConverter(typeof(KeepCaseStringJsonConverter))]</c> opts out of that (a property's
    /// own converter takes precedence over the ones in the options). For free text the user types
    /// and expects back unchanged: titles, descriptions, URLs, e-mail addresses.
    /// </summary>
    public sealed class KeepCaseStringJsonConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType switch
            {
                JsonTokenType.Null => null,
                JsonTokenType.String => reader.GetString(),
                // Same tolerance as the upper-casing converter: a number sent where text is expected.
                JsonTokenType.Number => JsonDocument.ParseValue(ref reader).RootElement.ToString(),
                _ => reader.GetString(),
            };

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value);
    }
}
