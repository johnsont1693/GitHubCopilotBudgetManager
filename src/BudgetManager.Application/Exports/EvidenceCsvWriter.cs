using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BudgetManager.Application.Exports;

public sealed record EvidenceCsvDocument(byte[] Content, string ContentSha256, int RowCount);

public static class EvidenceCsvWriter
{
    public static EvidenceCsvDocument Write(
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<object?>> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);
        if (headers.Count == 0 || headers.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("CSV headers must be non-empty.", nameof(headers));
        }

        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', headers.Append("row_sha256").Select(Escape)));
        var rowCount = 0;
        foreach (var row in rows)
        {
            if (row.Count != headers.Count)
            {
                throw new ArgumentException("Every CSV row must match the header count.", nameof(rows));
            }

            var values = row.Select(Format).ToArray();
            var canonical = JsonSerializer.Serialize(values);
            var rowHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
            builder.AppendLine(string.Join(',', values.Append(rowHash).Select(Escape)));
            rowCount++;
        }

        var content = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(builder.ToString());
        return new EvidenceCsvDocument(
            content,
            Convert.ToHexString(SHA256.HashData(content)),
            rowCount);
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        DateOnly dateOnly => dateOnly.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => value.ToString() ?? string.Empty,
    };

    private static string Escape(string value)
    {
        var protectedValue = value.Length > 0 && value[0] is '=' or '+' or '-' or '@'
            ? $"'{value}"
            : value;
        return $"\"{protectedValue.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
