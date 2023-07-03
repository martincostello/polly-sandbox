// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

using Polly;
using Polly.Bulkhead;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Refit;

namespace PollySandbox;

public partial class PolicyFactory
{
    private readonly PolicyStore _store;
    private readonly IMetricsPublisher _metricsPublisher;
    private readonly ILogger _logger;

    public PolicyFactory(
        PolicyStore store,
        IMetricsPublisher metricsPublisher,
        ILogger<PolicyFactory> logger)
    {
        _store = store;
        _metricsPublisher = metricsPublisher;
        _logger = logger;
    }

    public IAsyncPolicy GetPolicy(string token, ApiEndpointOption endpoint, string resource)
        => _store.GetPolicy(token, endpoint, resource);

    public IAsyncPolicy<TResult> GetPolicy<TResult>(
        string token,
        ApiEndpointOption endpoint,
        Func<TResult> fallbackValue,
        bool handleExecutionFaults,
        string resource)
    {
        var commonPolicy = _store.GetPolicy(token, endpoint, resource);

        var builder = Policy<TResult>
             .Handle<ApiException>()
             .OrHttpRequestFault()
             .Or<TaskCanceledException>();

        if (handleExecutionFaults)
        {
            builder = builder
             .Or<BulkheadRejectedException>()
             .Or<BrokenCircuitException>()
             .Or<IsolatedCircuitException>()
             .Or<TimeoutRejectedException>();
        }

        return builder
            .FallbackAsync((context, _) => Task.FromResult(fallbackValue != null ? fallbackValue() : default), OnFallbackAsync)
            .WithPolicyKey($"{endpoint.Name} Fallback")
            .WrapAsync(commonPolicy)
            .WithPolicyKey($"{endpoint.Name} Composite Fallback");
    }

    private Task OnFallbackAsync<TResult>(DelegateResult<TResult> fallback, Context context)
    {
        _metricsPublisher.Increment($"polly.fallback.{context.OperationKey?.ToLowerInvariant()}");

        Log.FallbackUsed(
            _logger,
            fallback.Exception,
            context.PolicyWrapKey,
            context.PolicyKey,
            context.OperationKey);

        return Task.CompletedTask;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static partial class Log
    {
        [LoggerMessage(1, LogLevel.Warning, "Fallback used for {PolicyWrapKey}:{PolicyKey}:{OperationKey}.")]
        public static partial void FallbackUsed(ILogger logger, Exception exception, string policyWrapKey, string policyKey, string operationKey);
    }
}
