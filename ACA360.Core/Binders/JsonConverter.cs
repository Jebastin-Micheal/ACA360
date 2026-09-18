using System;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

public class UppercaseStringJsonConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // 1. If it's a standard string, read it and uppercase it
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            return value?.ToUpper();
        }

        // 2. THE FIX: If it's a raw number token, extract its string value safely 
        // to prevent the "Cannot get the value of a token type 'Number' as a string" error.
        if (reader.TokenType == JsonTokenType.Number)
        {
            using (JsonDocument doc = JsonDocument.ParseValue(ref reader))
            {
                return doc.RootElement.ToString().ToUpper();
            }
        }

        // 3. Handle null tokens gracefully
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        // Fallback default implementation
        return reader.GetString()?.ToUpper();
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}