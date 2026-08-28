using System.Net;

namespace BudgetManager.Infrastructure.GitHub;

public sealed class GitHubApiException : HttpRequestException
{
    public GitHubApiException(
        string message,
        HttpStatusCode statusCode,
        string? requestId,
        string? documentationUrl = null)
        : base(message, null, statusCode)
    {
        RequestId = requestId;
        DocumentationUrl = documentationUrl;
    }

    public string? RequestId { get; }

    public string? DocumentationUrl { get; }
}
