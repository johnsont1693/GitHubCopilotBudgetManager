using System.Globalization;

namespace BudgetManager.Infrastructure.GitHub;

public sealed record GitHubApiOptions
{
    public const string DefaultApiVersion = "2026-03-10";

    public static readonly Uri DefaultBaseAddress = new("https://api.github.com/");

    public GitHubApiOptions(Uri? baseAddress = null, string apiVersion = DefaultApiVersion)
    {
        baseAddress ??= DefaultBaseAddress;
        ArgumentException.ThrowIfNullOrWhiteSpace(apiVersion);

        if (!baseAddress.IsAbsoluteUri || baseAddress.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("GitHub API base address must be an absolute HTTPS URI.", nameof(baseAddress));
        }

        if (!DateOnly.TryParseExact(
                apiVersion,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
        {
            throw new ArgumentException("GitHub API version must use yyyy-MM-dd format.", nameof(apiVersion));
        }

        BaseAddress = baseAddress;
        ApiVersion = apiVersion;
    }

    public Uri BaseAddress { get; }

    public string ApiVersion { get; }
}
