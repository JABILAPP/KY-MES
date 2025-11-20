using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace KY_MES.Application.App.Utils
{
    public class NullableInt32Converter : JsonConverter<int?>
    {
        public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int value))
                return value;

            if (reader.TokenType == JsonTokenType.String)
            {
                var str = reader.GetString();
                if (string.IsNullOrEmpty(str))
                    return null;
                if (int.TryParse(str, out int result))
                    return result;
                throw new JsonException($"Cannot convert string '{str}' to int?");
            }

            throw new JsonException($"Cannot convert token type {reader.TokenType} to int?");
        }

        public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
        {
            if (value.HasValue)
                writer.WriteNumberValue(value.Value);
            else
                writer.WriteNullValue();
        }
    }
}
