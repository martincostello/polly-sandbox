// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Polly;
using Polly.Bulkhead;
using Polly.CircuitBreaker;
using Polly.RateLimit;
using Polly.Timeout;
using Refit;
using Xunit.Abstractions;

namespace PollySandbox;

public class PolicyStoreTests
{
    public PolicyStoreTests(ITestOutputHelper outputHelper)
    {
        OutputHelper = outputHelper;
    }

    private ITestOutputHelper OutputHelper { get; }

    [Fact]
    public async Task PolicyStore_Applies_Timeout_With_CancellationToken()
    {
        // Arrange
        PolicyStore target = CreateStore();
        ApiEndpointOption endpoint = CreateValidEndpoint();
        string resource = string.Empty;

        endpoint.Timeout = TimeSpan.FromSeconds(1);

        // Act
        IAsyncPolicy policy = target.GetPolicy(string.Empty, endpoint, resource);

        // Assert
        policy.ShouldNotBeNull();
        policy.PolicyKey.ShouldContain(endpoint.Name);

        // Arrange
        using var source = new CancellationTokenSource();
        source.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            // Act and Assert
            await Assert.ThrowsAsync<TimeoutRejectedException>(
                () => policy.ExecuteAsync(() => Task.Delay(Timeout.Infinite, source.Token)));
        }
        finally
        {
            source.Cancel();
        }
    }

    [Fact]
    public async Task PolicyStore_Applies_Timeout_Without_CancellationToken()
    {
        // Arrange
        PolicyStore target = CreateStore();
        ApiEndpointOption endpoint = CreateValidEndpoint();
        string resource = string.Empty;

        endpoint.Timeout = TimeSpan.FromSeconds(1);

        // Act
        IAsyncPolicy policy = target.GetPolicy(string.Empty, endpoint, resource);

        // Assert
        policy.ShouldNotBeNull();
        policy.PolicyKey.ShouldContain(endpoint.Name);

        // Act and Assert
        await Assert.ThrowsAsync<TimeoutRejectedException>(
            () => policy.ExecuteAsync(() => Task.Delay(TimeSpan.FromSeconds(5))));
    }

    [Fact]
    public async Task PolicyStore_Applies_Bulkhead()
    {
        // Arrange
        PolicyStore target = CreateStore();
        ApiEndpointOption endpoint = CreateValidEndpoint();
        string resource = string.Empty;

        endpoint.MaxParallelization = 2;

        IAsyncPolicy policy = target.GetPolicy(string.Empty, endpoint, resource);

        // Act
        var tasks = Enumerable.Repeat(0, endpoint.MaxParallelization * 10)
            .Select((_) => policy.ExecuteAsync(() => Task.Delay(TimeSpan.FromMilliseconds(10))))
            .ToArray();

        // Assert
        await Assert.ThrowsAsync<BulkheadRejectedException>(() => Task.WhenAll(tasks));
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task PolicyStore_Applies_CircuitBreaker_To_Http_Errors(HttpStatusCode statusCode)
    {
        // Arrange
        PolicyStore target = CreateStore();
        ApiEndpointOption endpoint = CreateValidEndpoint();
        string resource = string.Empty;

        endpoint.FailureMinimumThroughput = 10;
        endpoint.FailureThreshold = 0.01;
        endpoint.FailureBreakDuration = TimeSpan.FromMinutes(1);
        endpoint.FailureSamplingDuration = TimeSpan.FromMinutes(1);

        IAsyncPolicy policy = target.GetPolicy(string.Empty, endpoint, resource);

        for (int i = 0; i < endpoint.FailureMinimumThroughput; i++)
        {
            await Assert.ThrowsAsync<ApiException>(() => policy.ExecuteAsync(() => ThrowsAsync(statusCode)));
        }

        // Act and Assert
        await Assert.ThrowsAsync<BrokenCircuitException>(
            () => policy.ExecuteAsync(() => Task.FromException(new HttpRequestException())));

        // Arrange - Verify that circuit breaker is reset
        endpoint.Isolate = false;

        // Act
        target.Clear();

        // Arrange
        policy = target.GetPolicy(string.Empty, endpoint, resource);

        // Assert
        await Assert.ThrowsAsync<ApiException>(() => policy.ExecuteAsync(() => ThrowsAsync(statusCode)));

        // Arrange - Force isolation of the circuit breaker
        endpoint.Isolate = true;

        // Act
        target.Clear();

        // Arrange
        policy = target.GetPolicy(string.Empty, endpoint, resource);

        // Assert
        await Assert.ThrowsAsync<IsolatedCircuitException>(() => policy.ExecuteAsync(() => ThrowsAsync(statusCode)));
    }

    [Fact]
    public async Task PolicyStore_Applies_CircuitBreaker_To_Rejected_Timeouts()
    {
        // Arrange
        PolicyStore target = CreateStore();
        ApiEndpointOption endpoint = CreateValidEndpoint();
        string resource = string.Empty;

        endpoint.MaxParallelization = int.MaxValue;
        endpoint.Timeout = TimeSpan.FromTicks(1);
        endpoint.FailureMinimumThroughput = 10;
        endpoint.FailureThreshold = 0.01;
        endpoint.FailureBreakDuration = TimeSpan.FromMinutes(1);
        endpoint.FailureSamplingDuration = TimeSpan.FromMinutes(1);

        IAsyncPolicy policy = target.GetPolicy(string.Empty, endpoint, resource);

        for (int i = 0; i < endpoint.FailureMinimumThroughput; i++)
        {
            await Assert.ThrowsAsync<TimeoutRejectedException>(
                () => policy.ExecuteAsync(() => Task.Delay(TimeSpan.FromSeconds(5))));
        }

        // Act and Assert
        await Assert.ThrowsAsync<BrokenCircuitException>(
            () => policy.ExecuteAsync(() => Task.Delay(TimeSpan.FromSeconds(5))));

        // Arrange - Verify that circuit breaker is reset
        endpoint.Isolate = false;

        // Act
        target.Clear();

        // Arrange
        policy = target.GetPolicy(string.Empty, endpoint, resource);

        // Assert
        await Assert.ThrowsAsync<TimeoutRejectedException>(
            () => policy.ExecuteAsync(() => Task.Delay(TimeSpan.FromSeconds(5))));

        // Arrange - Force isolation of the circuit breaker
        endpoint.Isolate = true;

        // Act
        target.Clear();

        // Arrange
        policy = target.GetPolicy(string.Empty, endpoint, resource);

        // Assert
        await Assert.ThrowsAsync<IsolatedCircuitException>(
            () => policy.ExecuteAsync(() => Task.Delay(TimeSpan.FromSeconds(5))));
    }

    [Fact]
    public async Task PolicyStore_Applies_CircuitBreaker_To_Canceled_Operations()
    {
        // Arrange
        PolicyStore target = CreateStore();
        ApiEndpointOption endpoint = CreateValidEndpoint();
        string resource = string.Empty;

        endpoint.MaxParallelization = int.MaxValue;
        endpoint.Timeout = TimeSpan.FromTicks(1);
        endpoint.FailureMinimumThroughput = 10;
        endpoint.FailureThreshold = 0.01;
        endpoint.FailureBreakDuration = TimeSpan.FromMinutes(1);
        endpoint.FailureSamplingDuration = TimeSpan.FromMinutes(1);

        IAsyncPolicy policy = target.GetPolicy(string.Empty, endpoint, resource);

        var cancellationToken = new CancellationToken(canceled: true);

        for (int i = 0; i < endpoint.FailureMinimumThroughput; i++)
        {
            await Assert.ThrowsAsync<TaskCanceledException>(
                () => policy.ExecuteAsync(() => Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)));
        }

        // Act and Assert
        await Assert.ThrowsAsync<BrokenCircuitException>(
            () => policy.ExecuteAsync(() => Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)));

        // Arrange - Verify that circuit breaker is reset
        endpoint.Isolate = false;

        // Act
        target.Clear();

        // Arrange
        policy = target.GetPolicy(string.Empty, endpoint, resource);

        // Assert
        await Assert.ThrowsAsync<TaskCanceledException>(
            () => policy.ExecuteAsync(() => Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)));

        // Arrange - Force isolation of the circuit breaker
        endpoint.Isolate = true;

        // Act
        target.Clear();

        // Arrange
        policy = target.GetPolicy(string.Empty, endpoint, resource);

        // Assert
        await Assert.ThrowsAsync<IsolatedCircuitException>(
            () => policy.ExecuteAsync(() => Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task PolicyStore_CircuitBreaker_Ignores_Client_Errors(HttpStatusCode statusCode)
    {
        // Arrange
        PolicyStore target = CreateStore();
        ApiEndpointOption endpoint = CreateValidEndpoint();
        string resource = string.Empty;

        endpoint.FailureMinimumThroughput = 10;
        endpoint.FailureThreshold = 0.01;
        endpoint.FailureBreakDuration = TimeSpan.FromMinutes(1);
        endpoint.FailureSamplingDuration = TimeSpan.FromMinutes(1);

        IAsyncPolicy policy = target.GetPolicy(string.Empty, endpoint, resource);

        for (int i = 0; i < endpoint.FailureMinimumThroughput; i++)
        {
            await Assert.ThrowsAsync<ApiException>(() => policy.ExecuteAsync(() => ThrowsAsync(statusCode)));
        }

        // Act and Assert
        await Assert.ThrowsAsync<ApiException>(() => policy.ExecuteAsync(() => ThrowsAsync(statusCode)));
    }

    [Fact]
    public async Task PolicyStore_Isolates_CircuitBreaker_If_Endpoint_Is_Isolated_On_Creation()
    {
        // Arrange
        PolicyStore target = CreateStore();
        ApiEndpointOption endpoint = CreateValidEndpoint();
        string resource = string.Empty;

        endpoint.Isolate = true;

        IAsyncPolicy policy = target.GetPolicy(string.Empty, endpoint, resource);

        // Assert
        await Assert.ThrowsAsync<IsolatedCircuitException>(() => policy.ExecuteAsync(ThrowsAsync<HttpRequestException>));

        // Arrange - Verify that circuit breaker is reset
        endpoint.Isolate = false;

        // Act
        target.Clear();

        // Arrange
        policy = target.GetPolicy(string.Empty, endpoint, resource);

        // Assert
        await Assert.ThrowsAsync<HttpRequestException>(() => policy.ExecuteAsync(ThrowsAsync<HttpRequestException>));
    }

    [Fact]
    public async Task PolicyStore_Shards_CircuitBreakers_By_Resource()
    {
        // Arrange
        PolicyStore target = CreateStore();
        ApiEndpointOption endpoint = CreateValidEndpoint();
        string resource1 = "a";
        string resource2 = "b";

        endpoint.FailureMinimumThroughput = 10;
        endpoint.FailureThreshold = 0.01;
        endpoint.FailureBreakDuration = TimeSpan.FromMinutes(1);
        endpoint.FailureSamplingDuration = TimeSpan.FromMinutes(1);

        IAsyncPolicy policy1 = target.GetPolicy(string.Empty, endpoint, resource1);
        IAsyncPolicy policy2 = target.GetPolicy(string.Empty, endpoint, resource2);

        for (int i = 0; i < endpoint.FailureMinimumThroughput; i++)
        {
            await Assert.ThrowsAsync<ApiException>(() => policy1.ExecuteAsync(() => ThrowsAsync(HttpStatusCode.InternalServerError)));
        }

        // Act and Assert - First resource has broken the circuit
        await Assert.ThrowsAsync<BrokenCircuitException>(
            () => policy1.ExecuteAsync(() => Task.FromException(new HttpRequestException())));

        // Act and Assert - Second resource is unaffected
        await Assert.ThrowsAsync<ApiException>(() => policy2.ExecuteAsync(() => ThrowsAsync(HttpStatusCode.InternalServerError)));
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
    public async Task PolicyStore_Applies_Retries_For_Http_Error(HttpStatusCode statusCode, int retries, int expected)
    {
        // Arrange
        PolicyStore target = CreateStore();
        ApiEndpointOption endpoint = CreateValidEndpoint(retries, failureMinimumThroughput: int.MaxValue);
        string resource = string.Empty;

        endpoint.Timeout = TimeSpan.FromSeconds(1);

        // Act
        IAsyncPolicy policy = target.GetPolicy(string.Empty, endpoint, resource);

        // Assert
        policy.ShouldNotBeNull();
        policy.PolicyKey.ShouldContain(endpoint.Name);

        int executions = 0;

        await Assert.ThrowsAsync<ApiException>(
            () => policy.ExecuteAsync(
                () =>
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
    public async Task PolicyStore_Applies_Retries_For_TaskCancellationException(int retries, int expected)
    {
        // Arrange
        PolicyStore target = CreateStore();
        ApiEndpointOption endpoint = CreateValidEndpoint(retries, failureMinimumThroughput: int.MaxValue);
        string resource = string.Empty;

        endpoint.Timeout = TimeSpan.FromSeconds(1);

        // Act
        IAsyncPolicy policy = target.GetPolicy(string.Empty, endpoint, resource);

        // Assert
        policy.ShouldNotBeNull();
        policy.PolicyKey.ShouldContain(endpoint.Name);

        int executions = 0;

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => policy.ExecuteAsync(
                () =>
                {
                    executions++;
                    return ThrowsAsync<TaskCanceledException>();
                }));

        // Assert
        executions.ShouldBe(expected);
    }

    [Fact]
    public async Task PolicyStore_Applies_Retries_And_Returns_Result()
    {
        // Arrange
        int retries = 1;
        int expected = 42;

        PolicyStore target = CreateStore();
        ApiEndpointOption endpoint = CreateValidEndpoint(retries);
        string resource = string.Empty;

        endpoint.Timeout = TimeSpan.FromSeconds(1);

        // Act
        IAsyncPolicy policy = target.GetPolicy(string.Empty, endpoint, resource);

        // Assert
        policy.ShouldNotBeNull();
        policy.PolicyKey.ShouldContain(endpoint.Name);

        int executions = 0;

        int actual = await policy.ExecuteAsync(
            async () =>
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
    public async Task PolicyStore_Applies_Rate_Limit()
    {
        // Arrange
        PolicyStore target = CreateStore();
        ApiEndpointOption endpoint = CreateValidEndpoint();
        string resource = string.Empty;

        endpoint.RateLimit = 1;
        endpoint.RateLimitBurst = 2;
        endpoint.RateLimitPeriod = TimeSpan.FromSeconds(1);

        string token1 = "foo";
        string token2 = "bar";

        IAsyncPolicy policy1 = target.GetPolicy(token1, endpoint, resource);
        IAsyncPolicy policy2 = target.GetPolicy(token2, endpoint, resource);

        static Task<bool> Action()
        {
            return Task.FromResult(true);
        }

        // Act
        for (int i = 0; i < endpoint.RateLimitBurst; i++)
        {
            _ = await policy1.ExecuteAsync(Action);
        }

        // Assert
        await Assert.ThrowsAsync<RateLimitRejectedException>(() => policy1.ExecuteAsync(Action));
        (await policy2.ExecuteAsync(Action)).ShouldBeTrue();

        // Arrange - Wait for reset
        await Task.Delay(endpoint.RateLimitPeriod);

        // Act and Assert
        (await policy1.ExecuteAsync(Action)).ShouldBeTrue();
        (await policy2.ExecuteAsync(Action)).ShouldBeTrue();
    }

    private static Task ThrowsAsync<T>()
        where T : Exception, new()
    {
        return Task.FromException<T>(new T());
    }

    private static ApiEndpointOption CreateValidEndpoint(int retries = 0, int failureMinimumThroughput = 2)
    {
        return new ApiEndpointOption()
        {
            FailureBreakDuration = TimeSpan.FromSeconds(1),
            FailureMinimumThroughput = failureMinimumThroughput,
            FailureSamplingDuration = TimeSpan.FromSeconds(1),
            FailureThreshold = 1,
            MaxParallelization = 1,
            Name = "My-Endpoint",
            RateLimit = 0,
            RateLimitBurst = 1,
            RateLimitPeriod = TimeSpan.FromSeconds(30),
            Retries = retries,
            Timeout = TimeSpan.FromSeconds(1),
        };
    }

    private static async Task ThrowsAsync(HttpStatusCode statusCode)
    {
        var exception = await ApiException.Create(
            new HttpRequestMessage() { RequestUri = new Uri("https://polly-sandbox.corp.local") },
            HttpMethod.Get,
            new HttpResponseMessage(statusCode),
            new RefitSettings());

        throw exception;
    }

    private PolicyStore CreateStore()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var metricsPublisher = Mock.Of<IMetricsPublisher>();
        var random = new Random();
        var logger = OutputHelper.ToLogger<PolicyStore>();

        return new PolicyStore(memoryCache, metricsPublisher, random, logger);
    }
}
