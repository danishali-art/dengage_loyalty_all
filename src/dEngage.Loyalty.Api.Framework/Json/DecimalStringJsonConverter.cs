using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace dEngage.Loyalty.Api.Framework.Json;

// Money/points are numeric(20,4) server-side. The default System.Text.Json decimal
// converter writes bare JSON numbers, which risks precision loss for large values in
// JS-based clients (IEEE754 doubles) — the frontend's DecimalString convention expects
// every decimal on the wire as a JSON string instead, so this is applied to every decimal
// (and, via System.Text.Json's automatic Nullable<T> unwrapping, every decimal?) response
// and request field via JsonConventions.Options.
public sealed class DecimalStringJsonConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return decimal.Parse(reader.GetString()!, NumberStyles.Number, CultureInfo.InvariantCulture);
        return reader.GetDecimal();
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}
