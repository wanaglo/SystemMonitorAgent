using Microsoft.AspNetCore.Hosting;
using SystemMonitorAgent.Core.Models;
using SystemMonitorAgent.DemoApi.Services;

namespace SystemMonitorAgent.DemoApi;

public sealed class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        ConfigureUrls(builder);

        builder.Services.AddSingleton<ReceivedSnapshotStore>();

        var app = builder.Build();

        app.MapGet("/", (ReceivedSnapshotStore store) =>
        {
            var latest = store.GetLatest();

            return Results.Ok(new
            {
                service = "SystemMonitorAgent.DemoApi",
                status = "running",
                totalReceived = store.Count,
                latestReceivedAtUtc = latest?.ReceivedAtUtc,
                latestHostname = latest?.Snapshot.Hostname,
                endpoints = new
                {
                    health = "/health",
                    metricsPost = "/api/metrics",
                    latest = "/api/metrics/latest",
                    recent = "/api/metrics/recent?take=10"
                }
            });
        });

        app.MapGet("/health", () => Results.Ok(new
        {
            status = "ok",
            utcNow = DateTimeOffset.UtcNow
        }));

        app.MapPost("/api/metrics", (
            SystemSnapshot snapshot,
            ReceivedSnapshotStore store,
            ILogger<Program> logger) =>
        {
            var receivedSnapshot = store.Add(snapshot);

            logger.LogInformation(
                "Получен системный снимок. Hostname: {Hostname}, CollectedAtUtc: {CollectedAtUtc}, ReceivedAtUtc: {ReceivedAtUtc}, TotalReceived: {TotalReceived}",
                snapshot.Hostname,
                snapshot.CollectedAtUtc,
                receivedSnapshot.ReceivedAtUtc,
                store.Count);

            return Results.Ok(new
            {
                message = "Snapshot accepted",
                receivedAtUtc = receivedSnapshot.ReceivedAtUtc,
                totalReceived = store.Count
            });
        });

        app.MapGet("/api/metrics/latest", (ReceivedSnapshotStore store) =>
        {
            var latest = store.GetLatest();
            return latest is null
                ? Results.NotFound(new { message = "Снимки ещё не получены." })
                : Results.Ok(latest);
        });

        app.MapGet("/api/metrics/recent", (int? take, ReceivedSnapshotStore store) =>
        {
            return Results.Ok(store.GetRecent(take ?? 10));
        });

        app.Run();
    }

    private static void ConfigureUrls(WebApplicationBuilder builder)
    {
        var configuredUrls = builder.Configuration[WebHostDefaults.ServerUrlsKey];
        if (string.IsNullOrWhiteSpace(configuredUrls))
        {
            builder.WebHost.UseUrls("http://localhost:5000");
        }
    }
}
