using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BudgetManager.Domain.Classification;

namespace BudgetManager.Application.Classification;

public sealed record StoredPolicy(
    Guid Id,
    int Version,
    string Mode,
    string Governance,
    string ScopeKind,
    string ScopeExternalId,
    string DefinitionJson);

public sealed record ClassificationTarget(
    string ScopeKind,
    string ScopeExternalId,
    IReadOnlyList<MetricObservation> Observations);

public sealed record ClassificationSnapshot(
    Guid PolicyId,
    int PolicyVersion,
    string ScopeKind,
    string ScopeExternalId,
    ClassificationResult Result,
    string InputsFingerprint,
    DateTimeOffset EvaluatedAt);

public sealed record ClassificationRunResult(int PolicyCount, int EvaluationCount, int UnknownCount);

public interface IClassificationStore
{
    Task<IReadOnlyList<StoredPolicy>> GetActivePoliciesAsync(Guid enterpriseId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClassificationTarget>> GetTargetsAsync(Guid enterpriseId, StoredPolicy policy, CancellationToken cancellationToken = default);

    Task SaveAsync(Guid enterpriseId, IReadOnlyList<ClassificationSnapshot> snapshots, CancellationToken cancellationToken = default);
}

public sealed class ClassificationRunService
{
    private readonly IClassificationStore store;
    private readonly TimeProvider timeProvider;

    public ClassificationRunService(IClassificationStore store, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        this.store = store;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ClassificationRunResult> RunAsync(Guid enterpriseId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(enterpriseId, Guid.Empty);
        var policies = await store.GetActivePoliciesAsync(enterpriseId, cancellationToken);
        var snapshots = new List<ClassificationSnapshot>();
        var evaluatedAt = timeProvider.GetUtcNow();
        var evaluatedOn = DateOnly.FromDateTime(evaluatedAt.UtcDateTime);

        foreach (var policy in policies)
        {
            var targets = await store.GetTargetsAsync(enterpriseId, policy, cancellationToken);
            foreach (var target in targets)
            {
                var result = Evaluate(policy, target.Observations, evaluatedOn);
                snapshots.Add(new ClassificationSnapshot(
                    policy.Id,
                    policy.Version,
                    target.ScopeKind,
                    target.ScopeExternalId,
                    result,
                    CreateFingerprint(policy, target),
                    evaluatedAt));
            }
        }

        await store.SaveAsync(enterpriseId, snapshots.AsReadOnly(), cancellationToken);
        return new ClassificationRunResult(
            policies.Count,
            snapshots.Count,
            snapshots.Count(item => item.Result.Status == HealthStatus.Unknown));
    }

    private static ClassificationResult Evaluate(StoredPolicy policy, IReadOnlyList<MetricObservation> observations, DateOnly evaluatedOn)
    {
        using var document = JsonDocument.Parse(policy.DefinitionJson);
        var root = document.RootElement;
        return policy.Mode.Equals("Weighted", StringComparison.OrdinalIgnoreCase)
            ? WeightedClassifier.Evaluate(ParseWeighted(root), observations, evaluatedOn)
            : PrecedenceClassifier.Evaluate(ParsePrecedence(root), observations, evaluatedOn);
    }

    private static WeightedClassificationPolicy ParseWeighted(JsonElement root)
    {
        var rules = root.GetProperty("metricRules").EnumerateArray().Select(item => new WeightedMetricRule(
            item.GetProperty("metricKey").GetString()!,
            item.GetProperty("weight").GetDecimal(),
            item.GetProperty("poorThreshold").GetDecimal(),
            item.GetProperty("goodThreshold").GetDecimal(),
            Enum.Parse<MetricDirection>(item.GetProperty("direction").GetString()!, true),
            item.GetProperty("maximumAgeDays").GetInt32(),
            !item.TryGetProperty("isRequired", out var required) || required.GetBoolean()));
        return new WeightedClassificationPolicy(
            rules,
            root.GetProperty("yellowMinimumScore").GetDecimal(),
            root.GetProperty("greenMinimumScore").GetDecimal());
    }

    private static PrecedenceClassificationPolicy ParsePrecedence(JsonElement root)
    {
        var requirements = root.GetProperty("requiredMetrics").EnumerateArray().Select(item => new MetricRequirement(
            item.GetProperty("metricKey").GetString()!,
            item.GetProperty("maximumAgeDays").GetInt32()));
        var rules = root.GetProperty("rules").EnumerateArray().Select(item => new PrecedenceRule(
            item.GetProperty("id").GetString()!,
            item.GetProperty("metricKey").GetString()!,
            Enum.Parse<MetricComparison>(item.GetProperty("comparison").GetString()!, true),
            item.GetProperty("threshold").GetDecimal(),
            Enum.Parse<HealthStatus>(item.GetProperty("result").GetString()!, true),
            item.GetProperty("maximumAgeDays").GetInt32(),
            item.GetProperty("description").GetString()!));
        return new PrecedenceClassificationPolicy(
            requirements,
            rules,
            Enum.Parse<HealthStatus>(root.GetProperty("defaultStatus").GetString()!, true));
    }

    private static string CreateFingerprint(StoredPolicy policy, ClassificationTarget target)
    {
        var input = new StringBuilder()
            .Append(policy.Id).Append('|').Append(policy.Version).Append('|')
            .Append(target.ScopeKind).Append('|').Append(target.ScopeExternalId);
        foreach (var observation in target.Observations.OrderBy(item => item.MetricKey, StringComparer.Ordinal))
        {
            input.Append('|').Append(observation.MetricKey).Append('=').Append(observation.Value)
                .Append('@').Append(observation.ObservedOn).Append(':').Append(observation.Availability);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input.ToString())));
    }
}
