namespace BudgetManager.Domain.Classification;

public enum MetricAvailability
{
    Available = 0,
    Missing = 1,
    Suppressed = 2,
}

public sealed record MetricObservation
{
    public MetricObservation(
        string metricKey,
        decimal? value,
        DateOnly observedOn,
        MetricAvailability availability = MetricAvailability.Available,
        string? availabilityDetail = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metricKey);

        if (availability == MetricAvailability.Available && value is null)
        {
            throw new ArgumentException("An available metric must have a value.", nameof(value));
        }

        if (availability != MetricAvailability.Available && value is not null)
        {
            throw new ArgumentException("An unavailable metric cannot have a value.", nameof(value));
        }

        MetricKey = metricKey;
        Value = value;
        ObservedOn = observedOn;
        Availability = availability;
        AvailabilityDetail = availabilityDetail;
    }

    public string MetricKey { get; }

    public decimal? Value { get; }

    public DateOnly ObservedOn { get; }

    public MetricAvailability Availability { get; }

    public string? AvailabilityDetail { get; }
}
