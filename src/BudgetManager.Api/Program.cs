using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using BudgetManager.Api;
using BudgetManager.Api.Contracts;
using BudgetManager.Api.Endpoints;
using BudgetManager.Api.Security;
using BudgetManager.Application.Budgets;
using BudgetManager.Application.Messaging;
using BudgetManager.Application.Operations;
using BudgetManager.Domain.Budgets;
using BudgetManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var applicationInsightsConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(applicationInsightsConnectionString))
{
    var credentialOptions = new DefaultAzureCredentialOptions
    {
        ManagedIdentityClientId = builder.Configuration["AZURE_CLIENT_ID"],
    };
    builder.Services.AddOpenTelemetry().UseAzureMonitor(options =>
    {
        options.ConnectionString = applicationInsightsConnectionString;
        options.Credential = new DefaultAzureCredential(credentialOptions);
    });
}

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

var databaseProvider = builder.Configuration["Database:Provider"] ?? "SqlServer";
var connectionString = builder.Configuration.GetConnectionString("BudgetManager")
    ?? throw new InvalidOperationException("ConnectionStrings:BudgetManager is required.");
if (databaseProvider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddDbContext<BudgetManagerDbContext>(options => options.UseSqlite(connectionString));
}
else if (databaseProvider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddBudgetManagerSqlPersistence(connectionString);
}
else
{
    throw new InvalidOperationException("Database:Provider must be SqlServer or Sqlite.");
}

var authenticationEnabled = builder.Configuration.GetValue("Authentication:Enabled", true);
if (authenticationEnabled)
{
    var authority = builder.Configuration["Authentication:Authority"];
    var audience = builder.Configuration["Authentication:Audience"];
    if (string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(audience))
    {
        throw new InvalidOperationException(
            "Authentication:Authority and Authentication:Audience are required when authentication is enabled.");
    }

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = authority;
            options.Audience = audience;
            options.RequireHttpsMetadata = true;
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                NameClaimType = "name",
                RoleClaimType = "roles",
                ValidateAudience = true,
                ValidateIssuer = true,
                ValidateLifetime = true,
            };
        });
}

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(ApiPolicies.EnterpriseAdmin, policy => policy.RequireRole(ApiRoles.EnterpriseAdmin))
    .AddPolicy(ApiPolicies.Operator, policy => policy.RequireRole(
        ApiRoles.EnterpriseAdmin,
        ApiRoles.Operator))
    .AddPolicy(ApiPolicies.Auditor, policy => policy.RequireRole(
        ApiRoles.EnterpriseAdmin,
        ApiRoles.Operator,
        ApiRoles.Auditor));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(ApiRateLimits.AdministrativeWrites, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirst("oid")?.Value
                ?? context.User.FindFirst("sub")?.Value
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "unknown-client",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 20,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1),
            }));
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new BudgetIncreaseGuardrailPolicy(
    builder.Configuration.GetValue<long>("BudgetGuardrails:MaximumIncreaseAmount"),
    builder.Configuration.GetValue<decimal>("BudgetGuardrails:MaximumIncreasePercent"),
    builder.Configuration.GetValue<long>("BudgetGuardrails:MaximumCumulativeMonthlyIncrease"),
    builder.Configuration.GetValue<decimal>("BudgetGuardrails:ForecastHeadroomPercent"),
    TimeSpan.FromHours(builder.Configuration.GetValue<double>("BudgetGuardrails:CooldownHours")),
    TimeSpan.FromHours(builder.Configuration.GetValue<double>("BudgetGuardrails:MaximumDataAgeHours"))));
builder.Services.AddSingleton(new BudgetChangeApiOptions(TimeSpan.FromHours(
    builder.Configuration.GetValue<double>("BudgetGuardrails:ApprovalLifetimeHours", 48))));
builder.Services.AddSingleton(new WorkflowEventOptions(
    builder.Configuration["WorkflowEvents:DashboardUrl"],
    (builder.Configuration["WorkflowEvents:AdminPrincipalNames"] ?? string.Empty)
        .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
    builder.Configuration.GetValue(
        "PUBLISH_BUDGET_LIFECYCLE_EVENTS",
        builder.Configuration.GetValue("WorkflowEvents:PublishBudgetLifecycleEvents", true))));
builder.Services.AddScoped<IBudgetChangeRepository, EfBudgetChangeRepository>();
builder.Services.AddScoped<BudgetChangeProposalService>();
builder.Services.AddScoped<IRetentionStore, EfRetentionStore>();
builder.Services.AddScoped<RetentionService>();
builder.Services.AddScoped<IBaselineReconciliationStore, EfBaselineReconciliationStore>();
builder.Services.AddScoped<BaselineReconciliationService>();

var app = builder.Build();
if (!authenticationEnabled && !app.Environment.IsDevelopment())
{
    throw new InvalidOperationException("Authentication can only be disabled in Development.");
}

if (builder.Configuration.GetValue("Database:Initialize", false))
{
    await DatabaseInitializer.InitializeAsync(
        app.Services,
        databaseProvider,
        builder.Configuration.GetValue("Database:SeedDemoData", false));
}

app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    context.Response.Headers["Permissions-Policy"] = "camera=(), geolocation=(), microphone=()";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    if (!app.Environment.IsDevelopment())
    {
        context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    }

    if (!context.Request.Path.StartsWithSegments("/api")
        && !context.Request.Path.StartsWithSegments("/healthz")
        && !context.Request.Path.StartsWithSegments("/readyz"))
    {
        context.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; connect-src 'self' https://login.microsoftonline.com; img-src 'self' data:; style-src 'self'; style-src-attr 'unsafe-inline'; script-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    }

    await next(context);
});
var dashboardRoot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
if (!File.Exists(Path.Combine(dashboardRoot, "index.html")))
{
    dashboardRoot = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "dashboard"));
}

var dashboardProvider = new PhysicalFileProvider(dashboardRoot);
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = dashboardProvider });
app.UseStaticFiles(new StaticFileOptions { FileProvider = dashboardProvider });
if (authenticationEnabled)
{
    app.UseAuthentication();
}

app.UseRateLimiter();
app.UseAuthorization();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/readyz", async (BudgetManagerDbContext dbContext, CancellationToken cancellationToken) =>
    await dbContext.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "ready" })
        : Results.Problem(statusCode: 503, title: "Database is unavailable"));
app.MapGet("/api/v1", (ClaimsPrincipal user) => Results.Ok(new
{
    name = "GitHub Copilot Budget Manager API",
    version = "v1",
    authenticated = user.Identity?.IsAuthenticated ?? false,
}));

var evaluations = app.MapGroup("/api/v1/evaluations").MapEvaluationEndpoints();
var administration = app.MapGroup("/api/v1").MapAdministrationEndpoints(authenticationEnabled);
if (authenticationEnabled)
{
    evaluations.RequireAuthorization(ApiPolicies.Operator);
    administration.RequireAuthorization(ApiPolicies.Auditor);
}

app.Run();

public partial class Program;
