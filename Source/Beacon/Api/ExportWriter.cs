using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Beacon.Api;

/// <summary>
/// Writes export rows as CSV, JSON, XML or YAML with one schema: camelCase keys, permission and custom field
/// names unchanged, booleans as true/false and timestamps as UTC ISO 8601 (yyyy-MM-ddTHH:mm:ssZ).
/// </summary>
internal static partial class ExportWriter
{
    public static readonly string[] Formats = ["csv", "json", "xml", "yaml"];

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Row of ordered fields; a nested Dictionary is a group (consent, customFields, tracking).</summary>
    public sealed class Row : List<KeyValuePair<string, object?>>
    {
        public void Add(string key, object? value) => Add(new KeyValuePair<string, object?>(key, value));
    }

    public static string Timestamp(DateTime value) =>
        DateTime.SpecifyKind(value, value.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : value.Kind)
            .ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static IResult Write(HttpContext context, string fileName, string format, string rootName, string itemName, IReadOnlyList<Row> rows)
    {
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}.{format}\"";
        return format switch
        {
            "json" => Results.Text(JsonSerializer.Serialize(rows.Select(ToJsonObject), Json), "application/json", Encoding.UTF8),
            "xml" => Results.Text(ToXml(rootName, itemName, rows), "application/xml", Encoding.UTF8),
            "yaml" => Results.Text(ToYaml(rootName, rows), "application/yaml", Encoding.UTF8),
            // BOM: Excel reads CSV without one as the system code page.
            _ => Results.Text("\uFEFF" + ToCsv(rows), "text/csv", Encoding.UTF8)
        };
    }

    private static Dictionary<string, object?> ToJsonObject(IEnumerable<KeyValuePair<string, object?>> fields) =>
        fields.ToDictionary(f => f.Key, f => f.Value is IEnumerable<KeyValuePair<string, object?>> group ? ToJsonObject(group) : f.Value);

    // Nested groups flatten to "group.key" columns; columns are the union of all rows, in first-seen order.
    internal static string ToCsv(IReadOnlyList<Row> rows)
    {
        var flat = rows.Select(r => Flatten(r, "").ToList()).ToList();
        var columns = flat.SelectMany(r => r.Select(f => f.Key)).Distinct().ToList();

        var sb = new StringBuilder();
        sb.Append(string.Join(',', columns.Select(AdminEndpoints.CsvEscape))).Append("\r\n");
        foreach (var row in flat)
        {
            var values = row.ToDictionary(f => f.Key, f => f.Value);
            sb.Append(string.Join(',', columns.Select(c => AdminEndpoints.CsvEscape(Scalar(values.GetValueOrDefault(c)))))).Append("\r\n");
        }
        return sb.ToString();
    }

    private static IEnumerable<KeyValuePair<string, object?>> Flatten(IEnumerable<KeyValuePair<string, object?>> fields, string prefix)
    {
        foreach (var (key, value) in fields)
        {
            if (value is IEnumerable<KeyValuePair<string, object?>> group)
                foreach (var nested in Flatten(group, $"{prefix}{key}."))
                    yield return nested;
            else
                yield return new($"{prefix}{key}", value);
        }
    }

    // Keys pass through XmlConvert.EncodeLocalName: custom field names may contain characters XML names reject.
    private static string ToXml(string rootName, string itemName, IReadOnlyList<Row> rows)
    {
        static XElement Element(string key, object? value) => value is IEnumerable<KeyValuePair<string, object?>> group
            ? new XElement(XmlConvert.EncodeLocalName(key), group.Select(f => Element(f.Key, f.Value)))
            : new XElement(XmlConvert.EncodeLocalName(key), Scalar(value));

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement(rootName, rows.Select(r => new XElement(itemName, r.Select(f => Element(f.Key, f.Value))))));
        using var writer = new Utf8StringWriter();
        doc.Save(writer);
        return writer.ToString();
    }

    // Strings are JSON-quoted: a JSON string is a valid YAML scalar and never retypes ("true", "123", "- x").
    internal static string ToYaml(string rootName, IReadOnlyList<Row> rows)
    {
        if (rows.Count == 0) return $"{rootName}: []\n";
        var sb = new StringBuilder();
        sb.Append(rootName).Append(":\n");
        foreach (var row in rows)
            AppendYaml(sb, row, "    ", "  - ");
        return sb.ToString();
    }

    private static void AppendYaml(StringBuilder sb, IEnumerable<KeyValuePair<string, object?>> fields, string indent, string firstIndent)
    {
        var first = true;
        foreach (var (key, value) in fields)
        {
            sb.Append(first ? firstIndent : indent).Append(PlainKey().IsMatch(key) ? key : JsonSerializer.Serialize(key)).Append(':');
            first = false;
            if (value is IEnumerable<KeyValuePair<string, object?>> group)
            {
                var items = group.ToList();
                if (items.Count == 0) { sb.Append(" {}\n"); continue; }
                sb.Append('\n');
                AppendYaml(sb, items, indent + "  ", indent + "  ");
            }
            else
            {
                sb.Append(' ').Append(value switch
                {
                    null => "null",
                    bool b => b ? "true" : "false",
                    int or long => Convert.ToString(value, CultureInfo.InvariantCulture),
                    _ => JsonSerializer.Serialize(Scalar(value))
                }).Append('\n');
            }
        }
    }

    private static string Scalar(object? value) => value switch
    {
        null => "",
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z_][A-Za-z0-9_-]*$")]
    private static partial System.Text.RegularExpressions.Regex PlainKey();

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
