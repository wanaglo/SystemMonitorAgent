using System.Net;

namespace SystemMonitorAgent.Application.Delivery;

public enum SnapshotDeliveryOutcome
{
    Success,
    RetryableFailure,
    PermanentFailure
}

public sealed record SnapshotDeliveryResult
{
    private SnapshotDeliveryResult(
        SnapshotDeliveryOutcome outcome,
        string description,
        HttpStatusCode? statusCode)
    {
        Outcome = outcome;
        Description = description;
        StatusCode = statusCode;
    }

    public SnapshotDeliveryOutcome Outcome { get; }

    public string Description { get; }

    public HttpStatusCode? StatusCode { get; }

    public bool IsSuccess => Outcome == SnapshotDeliveryOutcome.Success;

    public bool ShouldRetry => Outcome == SnapshotDeliveryOutcome.RetryableFailure;

    public static SnapshotDeliveryResult Success()
    {
        return new SnapshotDeliveryResult(
            SnapshotDeliveryOutcome.Success,
            "Delivered successfully",
            statusCode: null);
    }

    public static SnapshotDeliveryResult RetryableFailure(
        string description,
        HttpStatusCode? statusCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        return new SnapshotDeliveryResult(
            SnapshotDeliveryOutcome.RetryableFailure,
            description,
            statusCode);
    }

    public static SnapshotDeliveryResult PermanentFailure(
        string description,
        HttpStatusCode? statusCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        return new SnapshotDeliveryResult(
            SnapshotDeliveryOutcome.PermanentFailure,
            description,
            statusCode);
    }
}
