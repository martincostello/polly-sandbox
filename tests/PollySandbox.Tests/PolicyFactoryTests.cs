// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

using System.Net;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using Refit;
using Xunit.Abstractions;

namespace PollySandbox;

public class PolicyFactoryTests
{
    public PolicyFactoryTests(ITestOutputHelper outputHelper)
    {
        OutputHelper = outputHelper;
    }

    private ITestOutputHelper OutputHelper { get; }

    [Fact]
    public async Task PolicyFactory_Basic_Returns_Policy_With_No_Fallback()
    {
        // Arrange
        PolicyFactory target = CreateFactory();
        ApiEndpointOption endpoint = CreateValidEndpoint();
        string resource = string.Empty;

        // Act
        var policy = target.GetPolicy(string.Empty, endpoint, resource);

        // Assert
        policy.ShouldNotBeNull();
        policy.PolicyKey.ShouldContain(endpoint.Name);

        // Act and Assert
        await Assert.ThrowsAsync<ApiException>(() => policy.ExecuteAsync(() => ThrowsBadRequestAsync()));
    }

    [Fact]
    public async Task PolicyFactory_Returns_Policy_Default_With_No_Fallback()
    {
        // Arrange
        PolicyFactory target = CreateFactory();
        ApiEndpointOption endpoint = CreateValidEndpoint();
        string resource = string.Empty;

        // Act
        var policy = target.GetPolicy<int>(string.Empty, endpoint, null, false, resource);

        // Assert
        policy.ShouldNotBeNull();
        policy.PolicyKey.ShouldContain(endpoint.Name);

        // Act
        int result = await policy.ExecuteAsync(() => ThrowsBadRequestAsync());

        // Assert
        result.ShouldBe(0);
    }

    [Fact]
    public async Task PolicyFactory_Returns_Policy_Result_If_No_Error()
    {
        // Arrange
        PolicyFactory target = CreateFactory();
        ApiEndpointOption endpoint = CreateValidEndpoint();
        string resource = string.Empty;

        // Act
        var policy = target.GetPolicy(string.Empty, endpoint, () => 5, true, resource);

        // Assert
        policy.ShouldNotBeNull();
        policy.PolicyKey.ShouldContain(endpoint.Name);

        // Act
        int actual = await policy.ExecuteAsync(() => Task.FromResult(10));

        // Assert
        actual.ShouldBe(10);
    }

    [Fact]
    public async Task PolicyFactory_Returns_Result_Type_Default_If_Error_And_No_Fallback()
    {
        // Arrange
        PolicyFactory target = CreateFactory();
        ApiEndpointOption endpoint = CreateValidEndpoint();
        string resource = string.Empty;

        // Act
        var policy = target.GetPolicy<int>(string.Empty, endpoint, null, true, resource);

        // Assert
        policy.ShouldNotBeNull();
        policy.PolicyKey.ShouldContain(endpoint.Name);

        // Act
        int actual = await policy.ExecuteAsync(() => ThrowsBadRequestAsync());

        // Assert
        actual.ShouldBe(0);
    }

    [Fact]
    public async Task PolicyFactory_Returns_Fallback_Value_If_Error()
    {
        // Arrange
        PolicyFactory target = CreateFactory();
        ApiEndpointOption endpoint = CreateValidEndpoint();
        string resource = string.Empty;

        // Act
        var policy = target.GetPolicy(string.Empty, endpoint, () => 42, true, resource);

        // Assert
        policy.ShouldNotBeNull();
        policy.PolicyKey.ShouldContain(endpoint.Name);

        // Act
        int actual = await policy.ExecuteAsync(() => ThrowsBadRequestAsync());

        // Assert
        actual.ShouldBe(42);
    }

    private static ApiEndpointOption CreateValidEndpoint()
    {
        return new ApiEndpointOption()
        {
            FailureBreakDuration = TimeSpan.FromSeconds(1),
            FailureMinimumThroughput = 2,
            FailureSamplingDuration = TimeSpan.FromSeconds(1),
            FailureThreshold = 1,
            MaxParallelization = 1,
            Name = "My-Endpoint",
            Timeout = TimeSpan.FromSeconds(1),
        };
    }

    private static async Task<int> ThrowsBadRequestAsync()
    {
        var exception = await ApiException.Create(
            new HttpRequestMessage() { RequestUri = new Uri("https://polly-sandbox.corp.local") },
            HttpMethod.Get,
            new HttpResponseMessage(HttpStatusCode.BadRequest),
            new RefitSettings());

        throw exception;
    }

    private PolicyFactory CreateFactory()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var metricsPublisher = Substitute.For<IMetricsPublisher>();
        var store = new PolicyStore(memoryCache, metricsPublisher, new Random(), OutputHelper.ToLogger<PolicyStore>());

        return new PolicyFactory(store, metricsPublisher, OutputHelper.ToLogger<PolicyFactory>());
    }
}
