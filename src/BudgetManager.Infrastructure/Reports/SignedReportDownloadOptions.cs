namespace BudgetManager.Infrastructure.Reports;

public sealed class SignedReportDownloadOptions
{
    public SignedReportDownloadOptions(IEnumerable<string> allowedHostSuffixes, long maximumBytes = 134_217_728)
    {
        ArgumentNullException.ThrowIfNull(allowedHostSuffixes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        var suffixes = allowedHostSuffixes
            .Select(item => item.Trim().TrimStart('.').ToLowerInvariant())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (suffixes.Length == 0)
        {
            throw new ArgumentException("At least one signed-report host suffix is required.", nameof(allowedHostSuffixes));
        }

        AllowedHostSuffixes = suffixes;
        MaximumBytes = maximumBytes;
    }

    public IReadOnlyList<string> AllowedHostSuffixes { get; }

    public long MaximumBytes { get; }
}
