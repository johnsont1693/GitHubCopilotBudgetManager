using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BudgetManager.Api.Tests;

public sealed class ApiAuthenticationTests
{
    [Fact]
    public async Task Health_endpoint_remains_anonymous_when_production_authentication_is_enabled()
    {
        using var environment = ConfigureEnvironment(authenticationEnabled: true);
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/healthz", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Administration_endpoint_rejects_anonymous_production_request()
    {
        using var environment = ConfigureEnvironment(authenticationEnabled: true);
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(
            $"/api/v1/budgets?enterpriseId={Guid.NewGuid():D}",
            UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_response_enforces_script_and_browser_capability_boundaries()
    {
        using var environment = ConfigureEnvironment(authenticationEnabled: true);
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/", UriKind.Relative));
        var content = await response.Content.ReadAsStringAsync();
        using var scriptResponse = await client.GetAsync(new Uri("/app.js?v=4", UriKind.Relative));
        var script = await scriptResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, scriptResponse.StatusCode);
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Contains(
            "script-src 'self'",
            Assert.Single(response.Headers.GetValues("Content-Security-Policy")),
            StringComparison.Ordinal);
        Assert.Contains(
            "style-src-attr 'unsafe-inline'",
            Assert.Single(response.Headers.GetValues("Content-Security-Policy")),
            StringComparison.Ordinal);
        Assert.Equal(
            "camera=(), geolocation=(), microphone=()",
            Assert.Single(response.Headers.GetValues("Permissions-Policy")));
        Assert.Equal(
            "max-age=31536000; includeSubDomains",
            Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
        Assert.DoesNotContain("<script>", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("src=\"theme.js", content, StringComparison.Ordinal);
        Assert.Contains("src=\"app.js?v=4\"", content, StringComparison.Ordinal);
        Assert.Contains("const form = event.currentTarget;", script, StringComparison.Ordinal);
        Assert.DoesNotContain("event.currentTarget.reset()", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Policy_actor_comes_from_host_identity_instead_of_request_body()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"budget-manager-api-{Guid.NewGuid():N}.db");
        try
        {
            using var environment = ConfigureEnvironment(
                authenticationEnabled: false,
                $"Data Source={databasePath};Pooling=False",
                initializeDatabase: true);
            using var factory = CreateFactory("Development");
            using var client = factory.CreateClient();
            var enterpriseId = Guid.NewGuid();

            using var response = await client.PostAsJsonAsync(
                new Uri("/api/v1/policies", UriKind.Relative),
                new
                {
                    enterpriseId,
                    name = "reference-policy",
                    mode = "weighted",
                    governance = "enforced",
                    scopeKind = "enterprise",
                    scopeExternalId = "contoso-demo",
                    definition = new { yellowMinimumScore = 40, greenMinimumScore = 70 },
                    actorObjectId = "forged-client-actor",
                });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("development-user", created.RootElement.GetProperty("createdBy").GetString());

            using var auditResponse = await client.GetAsync(new Uri(
                $"/api/v1/audit?enterpriseId={enterpriseId:D}&page=1&pageSize=10",
                UriKind.Relative));
            using var audit = JsonDocument.Parse(await auditResponse.Content.ReadAsStringAsync());
            var auditItem = Assert.Single(audit.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal("development-user", auditItem.GetProperty("actorId").GetString());
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Administrative_mutations_are_rate_limited_per_identity()
    {
        using var environment = ConfigureEnvironment(authenticationEnabled: false);
        using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();
        var invalidRequest = new
        {
            enterpriseId = Guid.Empty,
            name = string.Empty,
            mode = "weighted",
            governance = "enforced",
            scopeKind = "enterprise",
            scopeExternalId = "contoso-demo",
            definition = new { yellowMinimumScore = 40, greenMinimumScore = 70 },
        };

        for (var attempt = 0; attempt < 20; attempt++)
        {
            using var response = await client.PostAsJsonAsync(
                new Uri("/api/v1/policies", UriKind.Relative),
                invalidRequest);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using var rejected = await client.PostAsJsonAsync(
            new Uri("/api/v1/policies", UriKind.Relative),
            invalidRequest);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task Built_in_budget_proposal_route_rejects_automatic_mode()
    {
        using var environment = ConfigureEnvironment(authenticationEnabled: false);
        using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/budget-change-requests", UriKind.Relative),
            new
            {
                enterpriseId = Guid.NewGuid(),
                proposal = new
                {
                    budgetId = "budget-1",
                    proposedAmount = 1_100,
                },
                automaticMode = true,
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains(
            "requires a separate administrator approval",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Proposal_caller_cannot_override_server_owned_guardrails()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"budget-manager-api-{Guid.NewGuid():N}.db");
        try
        {
            using var environment = ConfigureEnvironment(
                authenticationEnabled: false,
                $"Data Source={databasePath};Pooling=False",
                initializeDatabase: true,
                seedDemoData: true);
            using var factory = CreateFactory("Development");
            using var client = factory.CreateClient();

            using var response = await client.PostAsJsonAsync(
                new Uri("/api/v1/budget-change-requests", UriKind.Relative),
                new
                {
                    enterpriseId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    policy = new
                    {
                        maximumIncreaseAmount = 1_000_000,
                        maximumIncreasePercent = 100,
                        maximumCumulativeMonthlyIncrease = 1_000_000,
                        forecastHeadroomPercent = 100,
                        cooldownHours = 0,
                        maximumDataAgeHours = 10_000,
                    },
                    proposal = new
                    {
                        budgetId = "budget-enterprise",
                        proposedAmount = 125_250,
                    },
                    automaticMode = false,
                });

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Contains("guardrails failed", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Manual_proposal_derives_evidence_from_persisted_budget_and_forecast()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"budget-manager-api-{Guid.NewGuid():N}.db");
        try
        {
            using var environment = ConfigureEnvironment(
                authenticationEnabled: false,
                $"Data Source={databasePath};Pooling=False",
                initializeDatabase: true,
                seedDemoData: true);
            using var factory = CreateFactory("Development");
            using var client = factory.CreateClient();
            var enterpriseId = Guid.Parse("11111111-1111-1111-1111-111111111111");

            using var response = await client.PostAsJsonAsync(
                new Uri("/api/v1/budget-change-requests", UriKind.Relative),
                new
                {
                    enterpriseId,
                    proposal = new
                    {
                        budgetId = "budget-enterprise",
                        proposedAmount = 125_050,
                    },
                    automaticMode = false,
                });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var requestId = created.RootElement.GetProperty("requestId").GetGuid();

            using var requestsResponse = await client.GetAsync(new Uri(
                $"/api/v1/budget-change-requests?enterpriseId={enterpriseId:D}&page=1&pageSize=100",
                UriKind.Relative));
            using var requests = JsonDocument.Parse(await requestsResponse.Content.ReadAsStringAsync());
            var persisted = requests.RootElement.GetProperty("items").EnumerateArray()
                .Single(item => item.GetProperty("id").GetGuid() == requestId);
            Assert.Equal("pendingApproval", persisted.GetProperty("status").GetString());
            Assert.Equal(125_000, persisted.GetProperty("expectedCurrentAmount").GetInt64());
            Assert.Equal(112_500m, persisted.GetProperty("forecastAmount").GetDecimal());
            Assert.Contains(
                "persisted-budget-and-forecast",
                persisted.GetProperty("evidenceJson").GetString(),
                StringComparison.Ordinal);
            Assert.Contains(
                "demo-forecast",
                persisted.GetProperty("evidenceJson").GetString(),
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public void Production_startup_rejects_disabled_authentication()
    {
        using var environment = ConfigureEnvironment(authenticationEnabled: false);
        using var factory = CreateFactory();

        var exception = Assert.Throws<InvalidOperationException>(factory.CreateClient);

        Assert.Contains("Authentication can only be disabled in Development", exception.ToString(), StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateFactory(string environmentName = "Production") =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment(environmentName));

    private static EnvironmentVariableScope ConfigureEnvironment(
        bool authenticationEnabled,
        string connectionString = "Data Source=:memory:",
        bool initializeDatabase = false,
        bool seedDemoData = false) => new(
        new Dictionary<string, string?>
        {
            ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = null,
            ["Authentication__Audience"] = "api://budget-manager-tests",
            ["Authentication__Authority"] = "https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111/v2.0",
            ["Authentication__Enabled"] = authenticationEnabled.ToString(),
            ["BudgetGuardrails__ApprovalLifetimeHours"] = "48",
            ["BudgetGuardrails__CooldownHours"] = "168",
            ["BudgetGuardrails__ForecastHeadroomPercent"] = "20",
            ["BudgetGuardrails__MaximumCumulativeMonthlyIncrease"] = "200",
            ["BudgetGuardrails__MaximumDataAgeHours"] = "24",
            ["BudgetGuardrails__MaximumIncreaseAmount"] = "100",
            ["BudgetGuardrails__MaximumIncreasePercent"] = "10",
            ["ConnectionStrings__BudgetManager"] = connectionString,
            ["Database__Initialize"] = initializeDatabase.ToString(),
            ["Database__Provider"] = "Sqlite",
            ["Database__SeedDemoData"] = seedDemoData.ToString(),
        });

    private static string TemporaryDatabasePath() => Path.Combine(
        Path.GetTempPath(),
        $"budget-manager-api-{Guid.NewGuid():N}.db");

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> previousValues;

        public EnvironmentVariableScope(IReadOnlyDictionary<string, string?> values)
        {
            previousValues = values.Keys.ToDictionary(
                key => key,
                Environment.GetEnvironmentVariable,
                StringComparer.Ordinal);
            foreach (var pair in values)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }

        public void Dispose()
        {
            foreach (var pair in previousValues)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }
}
