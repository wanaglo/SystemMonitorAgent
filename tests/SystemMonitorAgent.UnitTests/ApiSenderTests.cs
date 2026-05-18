using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http;
using SystemMonitorAgent.Core.Configuration;
using SystemMonitorAgent.Core.Models;
using SystemMonitorAgent.Infrastructure.Services;

namespace SystemMonitorAgent.UnitTests;

public class ApiSenderTests
{
    [Fact]
    public async Task SendAsync_ReturnsRetryableFailure_ForRetryableHttpStatusCode()
    {
        HttpRequestMessage? request = null;
        var sut = CreateSut((message, _) =>
        {
            request = message;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        });

        var result = await sut.SendAsync(CreateSnapshot());

        Assert.NotNull(request);
        Assert.Equal(HttpMethod.Post, request!.Method);
        Assert.True(result.ShouldRetry);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
    }

    [Fact]
    public async Task SendAsync_ReturnsPermanentFailure_ForNonRetryableHttpStatusCode()
    {
        var sut = CreateSut((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)));

        var result = await sut.SendAsync(CreateSnapshot());

        Assert.False(result.IsSuccess);
        Assert.False(result.ShouldRetry);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task SendAsync_ReturnsRetryableFailure_ForTransportException()
    {
        var sut = CreateSut((_, _) => throw new HttpRequestException("DNS failure"));

        var result = await sut.SendAsync(CreateSnapshot());

        Assert.True(result.ShouldRetry);
        Assert.Contains("Сетевая ошибка", result.Description);
    }

    [Fact]
    public async Task SendAsync_ReturnsRetryableFailure_ForTimeout()
    {
        var sut = CreateSut((_, _) => throw new TaskCanceledException("timeout"));

        var result = await sut.SendAsync(CreateSnapshot());

        Assert.True(result.ShouldRetry);
        Assert.Equal("HTTP-запрос превысил время ожидания", result.Description);
    }

    private static ApiSender CreateSut(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        var httpClient = new HttpClient(new DelegateHttpMessageHandler(handler));
        var factory = new StubHttpClientFactory(httpClient);

        return new ApiSender(
            factory,
            Options.Create(new AgentSettings
            {
                ApiUrl = "https://metrics.local/api/metrics",
                HttpTimeoutSeconds = 10
            }),
            NullLogger<ApiSender>.Instance);
    }

    private static SystemSnapshot CreateSnapshot()
    {
        return new SystemSnapshot
        {
            Hostname = "agent-host",
            CollectedAtUtc = DateTime.UtcNow
        };
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _httpClient;

        public StubHttpClientFactory(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public HttpClient CreateClient(string name)
        {
            return _httpClient;
        }
    }

    private sealed class DelegateHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public DelegateHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request, cancellationToken);
        }
    }
}
