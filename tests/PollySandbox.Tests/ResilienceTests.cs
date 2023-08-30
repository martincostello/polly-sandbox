// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;
using PollySandbox.Extensions;
using Refit;

namespace PollySandbox;

public class ResilienceTests
{
    public ResilienceTests(ITestOutputHelper outputHelper)
    {
        OutputHelper = outputHelper;
    }

    private ITestOutputHelper OutputHelper { get; }

    [Fact]
    public async Task Execution_Applies_Timeout_With_CancellationToken()
    {
        // Arrange
        (var client, _) = CreateClient((endpoint) => endpoint.Timeout = TimeSpan.FromSeconds(1));

        using var source = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act and Assert
        await Assert.ThrowsAsync<TimeoutRejectedException>(
            () => client.ExecuteAsync(
                (_) => Task.Delay(Timeout.Infinite, source.Token)));
    }

    [Fact]
    public async Task Execution_Applies_Timeout_Without_CancellationToken()
    {
        // Arrange
        (var client, _) = CreateClient((endpoint) => endpoint.Timeout = TimeSpan.FromSeconds(1));

        // Act and Assert
        var exception = await Assert.ThrowsAsync<TimeoutRejectedException>(
            () => client.ExecuteAsync(
                (token) => Task.Delay(TimeSpan.FromSeconds(5), token)));

        exception.Timeout.ShouldBe(TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Execution_Applies_CircuitBreaker_To_Http_Errors(HttpStatusCode statusCode)
    {
        // Arrange
        var failureMinimumThroughput = 2;
        var isolated = false;

        (var client, var configuration) = CreateClient((endpoint) =>
        {
            endpoint.FailureMinimumThroughput = failureMinimumThroughput;
            endpoint.Isolate = isolated;
        });

        for (int i = 0; i < failureMinimumThroughput; i++)
        {
            await Assert.ThrowsAsync<ApiException>(
                () => client.ExecuteAsync(
                    (_) => ThrowsAsync(statusCode)));
        }

        // Act and Assert
        await Assert.ThrowsAsync<BrokenCircuitException>(
            () => client.ExecuteAsync(
                (_) => Task.FromException(new HttpRequestException())));

        // Arrange - Verify that circuit breaker is reset
        configuration.Reload();

        // Assert
        await Assert.ThrowsAsync<ApiException>(
            () => client.ExecuteAsync(
                (_) => ThrowsAsync(statusCode)));

        // Arrange - Force isolation of the circuit breaker
        isolated = true;
        configuration.Reload();

        // Act and Assert
        await Assert.ThrowsAsync<IsolatedCircuitException>(
            () => client.ExecuteAsync(
                (_) => ThrowsAsync(statusCode)));
    }

    [Fact]
    public async Task Execution_Applies_CircuitBreaker_To_Rejected_Timeouts()
    {
        // Arrange
        var failureMinimumThroughput = 10;
        var isolated = false;

        (var client, var configuration) = CreateClient((endpoint) =>
        {
            endpoint.FailureBreakDuration = TimeSpan.FromMinutes(1);
            endpoint.FailureMinimumThroughput = failureMinimumThroughput;
            endpoint.FailureSamplingDuration = TimeSpan.FromMinutes(1);
            endpoint.FailureThreshold = 0.01;
            endpoint.Isolate = isolated;
            endpoint.Timeout = TimeSpan.FromTicks(1);
        });

        for (int i = 0; i < failureMinimumThroughput; i++)
        {
            await Assert.ThrowsAsync<TimeoutRejectedException>(
                () => client.ExecuteAsync(
                    (token) => Task.Delay(TimeSpan.FromSeconds(5), token)));
        }

        // Act and Assert
        await Assert.ThrowsAsync<BrokenCircuitException>(
            () => client.ExecuteAsync(
                (token) => Task.Delay(TimeSpan.FromSeconds(5), token)));

        // Arrange - Verify that circuit breaker is reset
        isolated = false;
        configuration.Reload();

        // Act and Assert
        await Assert.ThrowsAsync<TimeoutRejectedException>(
            () => client.ExecuteAsync(
                (token) => Task.Delay(TimeSpan.FromSeconds(5), token)));

        // Arrange - Force isolation of the circuit breaker
        isolated = true;
        configuration.Reload();

        // Act and Assert
        await Assert.ThrowsAsync<IsolatedCircuitException>(
            () => client.ExecuteAsync(
                (token) => Task.Delay(TimeSpan.FromSeconds(5), token)));
    }

    [Fact]
    public async Task Execution_Applies_CircuitBreaker_To_Canceled_Operations()
    {
        // Arrange
        var failureMinimumThroughput = 10;
        var isolated = false;

        (var client, var configuration) = CreateClient((endpoint) =>
        {
            endpoint.FailureBreakDuration = TimeSpan.FromMinutes(1);
            endpoint.FailureMinimumThroughput = failureMinimumThroughput;
            endpoint.FailureSamplingDuration = TimeSpan.FromMinutes(1);
            endpoint.FailureThreshold = 0.01;
            endpoint.Isolate = isolated;
            endpoint.Timeout = TimeSpan.FromTicks(1);
        });

        var cancellationToken = new CancellationToken(canceled: true);

        for (int i = 0; i < failureMinimumThroughput; i++)
        {
            await Assert.ThrowsAsync<TaskCanceledException>(
                () => client.ExecuteAsync(
                    (_) => Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)));
        }

        // Act and Assert
        await Assert.ThrowsAsync<BrokenCircuitException>(
            () => client.ExecuteAsync(
                (_) => Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)));

        // Arrange - Verify that circuit breaker is reset
        isolated = false;
        configuration.Reload();

        // Act and Assert
        await Assert.ThrowsAsync<TaskCanceledException>(
            () => client.ExecuteAsync(
                (_) => Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)));

        // Arrange - Force isolation of the circuit breaker
        isolated = true;
        configuration.Reload();

        // Act and Assert
        await Assert.ThrowsAsync<IsolatedCircuitException>(
            () => client.ExecuteAsync(
                (_) => Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Execution_CircuitBreaker_Ignores_Client_Errors(HttpStatusCode statusCode)
    {
        // Arrange
        var failureMinimumThroughput = 10;

        (var client, _) = CreateClient((endpoint) =>
        {
            endpoint.FailureBreakDuration = TimeSpan.FromMinutes(1);
            endpoint.FailureMinimumThroughput = failureMinimumThroughput;
            endpoint.FailureSamplingDuration = TimeSpan.FromMinutes(1);
            endpoint.FailureThreshold = 0.01;
        });

        for (int i = 0; i < failureMinimumThroughput; i++)
        {
            await Assert.ThrowsAsync<ApiException>(
                () => client.ExecuteAsync(
                    (_) => ThrowsAsync(statusCode)));
        }

        // Act and Assert
        await Assert.ThrowsAsync<ApiException>(
            () => client.ExecuteAsync(
                (_) => ThrowsAsync(statusCode)));
    }

    [Fact]
    public async Task Execution_Isolates_CircuitBreaker_If_Endpoint_Is_Isolated_On_Creation()
    {
        // Arrange
        var isolated = true;

        (var client, var configuration) = CreateClient((endpoint) => endpoint.Isolate = isolated);

        // Assert
        await Assert.ThrowsAsync<IsolatedCircuitException>(
            () => client.ExecuteAsync(ThrowsAsync<HttpRequestException>));

        // Arrange - Verify that circuit breaker is reset
        isolated = false;
        configuration.Reload();

        // Assert
        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.ExecuteAsync(ThrowsAsync<HttpRequestException>));
    }

    [Fact]
    public async Task Execution_Shards_CircuitBreakers_By_Resource()
    {
        // Arrange
        var failureMinimumThroughput = 10;

        (var client, _) = CreateClient((endpoint) =>
        {
            endpoint.FailureBreakDuration = TimeSpan.FromMinutes(1);
            endpoint.FailureMinimumThroughput = failureMinimumThroughput;
            endpoint.FailureSamplingDuration = TimeSpan.FromMinutes(1);
            endpoint.FailureThreshold = 0.01;
        });

        for (int i = 0; i < failureMinimumThroughput; i++)
        {
            await Assert.ThrowsAsync<ApiException>(
                () => client.ExecuteAsync(
                    (_) => ThrowsAsync(HttpStatusCode.InternalServerError)));
        }

        // Act and Assert - First resource has broken the circuit
        await Assert.ThrowsAsync<BrokenCircuitException>(
            () => client.ExecuteAsync(
                (_) => ThrowsAsync(HttpStatusCode.InternalServerError)));

        // Act and Assert - Second resource is unaffected
        var actual = await client.ExecuteWithFallbackAsync(
            (_) => ThrowsAsync(HttpStatusCode.InternalServerError),
            () => true);

        actual.ShouldBeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway, 0, 1)]
    [InlineData(HttpStatusCode.BadGateway, 1, 2)]
    [InlineData(HttpStatusCode.BadGateway, 2, 3)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 0, 1)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 1, 2)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 2, 3)]
    [InlineData(HttpStatusCode.GatewayTimeout, 0, 1)]
    [InlineData(HttpStatusCode.GatewayTimeout, 1, 2)]
    [InlineData(HttpStatusCode.GatewayTimeout, 2, 3)]
    [InlineData(HttpStatusCode.RequestTimeout, 0, 1)]
    [InlineData(HttpStatusCode.RequestTimeout, 1, 2)]
    [InlineData(HttpStatusCode.RequestTimeout, 2, 3)]
    public async Task Execution_Applies_Retries_For_Http_Error(HttpStatusCode statusCode, int retries, int expected)
    {
        // Arrange
        (var client, _) = CreateClient((endpoint) =>
        {
            endpoint.FailureMinimumThroughput = int.MaxValue;
            endpoint.Retries = retries;
            endpoint.Timeout = TimeSpan.FromSeconds(1);
        });

        var executions = 0;

        await Assert.ThrowsAsync<ApiException>(
            () => client.ExecuteAsync(
                (_) =>
                {
                    executions++;
                    return ThrowsAsync(statusCode);
                }));

        // Assert
        executions.ShouldBe(expected);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    public async Task Execution_Applies_Retries_For_TaskCancellationException(int retries, int expected)
    {
        // Arrange
        (var client, _) = CreateClient((endpoint) =>
        {
            endpoint.FailureMinimumThroughput = int.MaxValue;
            endpoint.Retries = retries;
            endpoint.Timeout = TimeSpan.FromSeconds(1);
        });

        var executions = 0;

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => client.ExecuteAsync(
                (_) =>
                {
                    executions++;
                    return ThrowsAsync<TaskCanceledException>(_);
                }));

        // Assert
        executions.ShouldBe(expected);
    }

    [Fact]
    public async Task Execution_Applies_Retries_And_Returns_Result()
    {
        // Arrange
        var retries = 1;
        var expected = 42;

        (var client, _) = CreateClient((endpoint) =>
        {
            endpoint.Retries = retries;
            endpoint.Timeout = TimeSpan.FromSeconds(1);
        });

        var executions = 0;

        int actual = await client.ExecuteAsync(
            async (_) =>
            {
                executions++;

                if (executions <= retries)
                {
                    await ThrowsAsync(HttpStatusCode.RequestTimeout);
                }

                return expected;
            });

        // Assert
        executions.ShouldBe(2);
        actual.ShouldBe(expected);
    }

    [Fact]
    public async Task Execution_Applies_Rate_Limit()
    {
        // Arrange
        var partition1 = "foo";
        var partition2 = "bar";

        var rateLimit = 1;
        var rateLimitPeriod = TimeSpan.FromSeconds(1);

        (var client, _) = CreateClient((endpoint) =>
        {
            endpoint.RateLimit = rateLimit;
            endpoint.RateLimitPeriod = rateLimitPeriod;
        });

        client.RateLimitPartition = () => partition1;

        // Act
        for (int i = 0; i < rateLimit; i++)
        {
            await client.ExecuteAsync(Action);
        }

        // Assert
        await Assert.ThrowsAsync<RateLimiterRejectedException>(
            () => client.ExecuteAsync(Action));

        client.RateLimitPartition = () => partition2;

        (await client.ExecuteAsync(Action)).ShouldBeTrue();

        // Arrange - Wait for reset
        await Task.Delay(rateLimitPeriod * 2);

        // Act and Assert
        client.RateLimitPartition = () => partition1;
        (await client.ExecuteAsync(Action)).ShouldBeTrue();

        client.RateLimitPartition = () => partition2;
        (await client.ExecuteAsync(Action)).ShouldBeTrue();
    }

    [Fact]
    public async Task Execution_Returns_Policy_Result_If_No_Error()
    {
        // Arrange
        (var client, _) = CreateClient();

        // Act
        int actual = await client.ExecuteWithFallbackAsync((_) => Task.FromResult(10));

        // Assert
        actual.ShouldBe(10);
    }

    [Fact]
    public async Task Execution_Returns_Result_Type_Default_If_Error_And_No_Fallback()
    {
        // Arrange
        (var client, _) = CreateClient();

        // Act
        int actual = await client.ExecuteWithFallbackAsync(
            (_) => ThrowsAsync<int>(HttpStatusCode.BadRequest));

        // Assert
        actual.ShouldBe(0);
    }

    [Fact]
    public async Task Execution_Returns_Fallback_Value_If_Error()
    {
        // Arrange
        (var client, _) = CreateClient();

        // Act
        int actual = await client.ExecuteWithFallbackAsync(
            (_) => ThrowsAsync<int>(HttpStatusCode.BadRequest),
            () => 42);

        // Assert
        actual.ShouldBe(42);
    }

    private static Task<bool> Action(CancellationToken _) => Task.FromResult(true);

    private static async Task ThrowsAsync<T>(CancellationToken _)
        where T : Exception, new()
    {
        await Task.FromException<T>(new T());
    }

    private static Task<bool> ThrowsAsync(HttpStatusCode statusCode)
        => ThrowsAsync<bool>(statusCode);

    private static async Task<TResult> ThrowsAsync<TResult>(HttpStatusCode statusCode)
    {
        var exception = await ApiException.Create(
            new HttpRequestMessage() { RequestUri = new Uri("https://polly-sandbox.corp.local") },
            HttpMethod.Get,
            new HttpResponseMessage(statusCode),
            new RefitSettings());

        throw exception;
    }

    private (TestClient Client, IConfigurationRoot Configuration) CreateClient(
        Action<ApiEndpointOption> configure = null)
    {
        var endpoint = new ApiEndpointOption()
        {
            FailureBreakDuration = TimeSpan.FromSeconds(1),
            FailureMinimumThroughput = 2,
            FailureSamplingDuration = TimeSpan.FromSeconds(1),
            FailureThreshold = 1,
            Isolate = false,
            Name = "Test",
            RateLimit = 0,
            RateLimitPeriod = TimeSpan.FromSeconds(30),
            Retries = 0,
            Timeout = TimeSpan.FromSeconds(1),
        };

        configure?.Invoke(endpoint);

        var endpoints = new Dictionary<string, IList<string>>
        {
            [endpoint.Name] = new[] { nameof(TestClient.ExecuteAsync), nameof(TestClient.ExecuteWithFallbackAsync) },
        };

        var configuration = new ConfigurationBuilder().Build();

        var services = new ServiceCollection()
            .AddHttpContextAccessor()
            .AddLogging(services => services.AddXUnit(OutputHelper))
            .AddResilience(endpoints);

        services.AddOptions()
                .Configure<ApiOptions>(configuration)
                .Configure<ApiOptions>((options) =>
                {
                    options.Endpoints = new Dictionary<string, ApiEndpointOption>()
                    {
                        [endpoint.Name] = endpoint,
                    };

                    configure?.Invoke(endpoint);
                });

        services.AddSingleton<IMetricsPublisher, MetricsPublisher>();
        services.AddSingleton<ResiliencePipelineFactory>();
        services.AddScoped((serviceProvider) => serviceProvider.GetRequiredService<IOptionsMonitor<ApiOptions>>().CurrentValue);
        services.AddTransient<TestClient>();

        var serviceProvider = services.BuildServiceProvider();
        var client = serviceProvider.GetRequiredService<TestClient>();

        return (client, configuration);
    }

    private sealed class TestClient : ApiClient
    {
        public TestClient(IHttpContextAccessor httpContextAccessor, ApiOptions options, ResiliencePipelineFactory pipelineFactory, ILogger<TestClient> logger)
            : base(httpContextAccessor, options, pipelineFactory, logger)
        {
        }

        public Func<string> RateLimitPartition { get; set; } = () => string.Empty;

        protected override ApiEndpointOption EndpointOption => Options.GetEndpoint("Test");

        protected override string OperationPrefix => "Test";

        public async Task ExecuteAsync(Func<CancellationToken, Task> act, CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(
                RateLimitPartition(),
                nameof(ExecuteAsync),
                async (token) =>
                {
                    await act(token);
                    return new ApiResponse<bool>(new HttpResponseMessage(HttpStatusCode.OK), true, new());
                },
                cancellationToken);
        }

        public async Task<TResult> ExecuteAsync<TResult>(Func<CancellationToken, Task<TResult>> act, CancellationToken cancellationToken = default)
        {
            return await ExecuteAsync(
                RateLimitPartition(),
                nameof(ExecuteAsync),
                async (token) =>
                {
                    var result = await act(token);
                    return new ApiResponse<TResult>(new HttpResponseMessage(HttpStatusCode.OK), result, new());
                },
                cancellationToken);
        }

        public async Task<TResult> ExecuteWithFallbackAsync<TResult>(
            Func<CancellationToken, Task<TResult>> act,
            Func<TResult> fallback = null,
            CancellationToken cancellationToken = default)
        {
            return await ExecuteAsync(
                RateLimitPartition(),
                nameof(ExecuteWithFallbackAsync),
                async (token) =>
                {
                    var result = await act(token);
                    return new ApiResponse<TResult>(new HttpResponseMessage(HttpStatusCode.OK), result, new());
                },
                new() { HandleExecutionFaults = true, FallbackValue = fallback },
                cancellationToken);
        }
    }
}
