using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using BudgetManager.Application.Identity;

namespace BudgetManager.Infrastructure.Identity;

public sealed class GraphDirectoryClient(
    HttpClient httpClient,
    TokenCredential credential,
    GraphDirectoryOptions options) : IEntraDirectoryClient
{
    private static readonly TokenRequestContext TokenRequest = new(["https://graph.microsoft.com/.default"]);

    public async Task<IReadOnlyList<EntraUserProfile>> GetUsersAsync(
        CancellationToken cancellationToken = default)
    {
        var users = new List<EntraUserProfile>();
        Uri? nextLink = new(
            "https://graph.microsoft.com/v1.0/users?$select=" +
            $"id,userPrincipalName,department,employeeOrgData,{options.GitHubLoginProperty}&$top=999");

        while (nextLink is not null)
        {
            if (nextLink.Scheme != Uri.UriSchemeHttps
                || !nextLink.Host.Equals("graph.microsoft.com", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Microsoft Graph returned an unexpected continuation URL.");
            }

            var accessToken = await credential.GetTokenAsync(TokenRequest, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, nextLink);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Token);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            foreach (var item in document.RootElement.GetProperty("value").EnumerateArray())
            {
                users.Add(new EntraUserProfile(
                    item.GetProperty("id").GetString()!,
                    GetString(item, "userPrincipalName"),
                    GetString(item, "department"),
                    GetNestedString(item, "employeeOrgData", "costCenter"),
                    GetString(item, options.GitHubLoginProperty)));
            }

            nextLink = document.RootElement.TryGetProperty("@odata.nextLink", out var next)
                && next.ValueKind == JsonValueKind.String
                ? new Uri(next.GetString()!)
                : null;
        }

        return users.AsReadOnly();
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string? GetNestedString(
        JsonElement element,
        string objectName,
        string propertyName) =>
        element.TryGetProperty(objectName, out var nested)
            ? GetString(nested, propertyName)
            : null;
}
