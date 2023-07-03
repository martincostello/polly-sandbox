// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

using System.Globalization;
using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Polly;
using Polly.Bulkhead;
using Polly.CircuitBreaker;
using Polly.Registry;
using Polly.Retry;
using Polly.Timeout;
using Refit;

namespace PollySandbox;

public partial class PolicyStore
{
    private readonly IMemoryCache _cache;
    private readonly ILogger _logger;
    private readonly Random _random;
    private readonly PolicyRegistry _registry;
    private readonly IMetricsPublisher _metricsPublisher;
    private readonly HashSet<string> _isolatedCircuits;

    public PolicyStore(
        IMemoryCache cache,
        IMetricsPublisher metricsPublisher,
        Random random,
        ILogger<PolicyStore> logger)
    {
        _cache = cache;
        _metricsPublisher = metricsPublisher;
        _random = random;
        _logger = logger;
        _isolatedCircuits = new HashSet<string>();
        _registry = new PolicyRegistry();
    }

    public IAsyncPolicy GetPolicy(string token, ApiEndpointOption endpoint, string resource)
    {
        AsyncPolicy breaker = GetCircuitBreaker(endpoint, resource);
        AsyncPolicy bulkhead = GetBulkhead(endpoint);
        AsyncPolicy timeout = GetTimeout(endpoint);
        AsyncPolicy retry = Policy.NoOpAsync().WithPolicyKey($"{endpoint.Name} No-Op");
        AsyncPolicy rateLimit = retry;

        if (endpoint.Retries > 0)
        {
            retry = GetRetry(endpoint);
        }

        if (endpoint.RateLimit > 0)
        {
            rateLimit = GetRateLimit(token, endpoint);
        }

        return Policy
            .WrapAsync(retry, breaker, timeout, bulkhead, rateLimit)
            .WithPolicyKey($"{endpoint.Name} Composite");
    }

    public void Clear()
    {
        _isolatedCircuits.Clear();
        _registry.Clear();
        Log.PoliciesReset(_logger);
    }

    public void Isolate(string endpointName)
        => _isolatedCircuits.Add(endpointName);

    private static bool CanCircuitBreak(ApiException exception)
    {
        return
            exception.StatusCode >= HttpStatusCode.InternalServerError ||
            exception.StatusCode == HttpStatusCode.RequestTimeout;
    }

    private static bool CanRetry(ApiException exception)
    {
        if (exception.RequestMessage.Method != HttpMethod.Get)
        {
            return false;
        }

        return exception.StatusCode switch
        {
            HttpStatusCode.BadGateway => true,
            HttpStatusCode.GatewayTimeout => true,
            HttpStatusCode.RequestTimeout => true,
            HttpStatusCode.ServiceUnavailable => true,
            _ => false,
        };
    }

    private IEnumerable<TimeSpan> DecorrelatedJitter(int maxRetries, TimeSpan seedDelay, TimeSpan maxDelay)
    {
        int retries = 0;

        double seed = seedDelay.TotalMilliseconds;
        double max = maxDelay.TotalMilliseconds;
        double current = seed;

        while (++retries <= maxRetries)
        {
            // From documentation here: https://github.com/App-vNext/Polly/wiki/Retry-with-jitter.
            // Based on sample code given here: https://gist.github.com/reisenberger/a8d5806a95523f936d56a6d1dedc70a1.
            // Adopting the "Decorrelated Jitter" formula from https://www.awsarchitectureblog.com/2015/03/backoff.html.
            // Can be between seed and current * 3.  Must not exceed maxDelay.
            current = Math.Min(max, Math.Max(seed, current * 3 * _random.NextDouble()));

            yield return TimeSpan.FromMilliseconds(current);
        }
    }

    private AsyncBulkheadPolicy GetBulkhead(ApiEndpointOption endpoint)
    {
        string key = $"{endpoint.Name}-Bulkhead";

        if (!_registry.TryGet(key, out AsyncBulkheadPolicy policy))
        {
            policy = Policy.BulkheadAsync(endpoint.MaxParallelization, OnBulkheadRejectedAsync);
            policy.WithPolicyKey($"{endpoint.Name} Bulkhead");

            _registry[key] = policy;
        }

        return policy;
    }

    private AsyncCircuitBreakerPolicy GetCircuitBreaker(ApiEndpointOption endpoint, string resource)
    {
        string key = $"{endpoint.Name}-{resource}-CircuitBreaker";

        if (!_registry.TryGet(key, out AsyncCircuitBreakerPolicy policy))
        {
            policy = Policy
                .Handle<ApiException>(CanCircuitBreak)
                .OrHttpRequestFault()
                .Or<OperationCanceledException>()
                .Or<TimeoutRejectedException>()
                .AdvancedCircuitBreakerAsync(
                    endpoint.FailureThreshold,
                    endpoint.FailureSamplingDuration,
                    endpoint.FailureMinimumThroughput,
                    endpoint.FailureBreakDuration,
                    OnCircuitBreak,
                    OnCircuitReset);

            policy.WithPolicyKey($"{endpoint.Name} Circuit-Breaker");

            if (endpoint.Isolate || _isolatedCircuits.Contains(endpoint.Name))
            {
                Log.CircuitIsolated(_logger, endpoint.Name);
                policy.Isolate();
            }

            _registry[key] = policy;
        }

        return policy;
    }

    private AsyncPolicy GetRateLimit(string token, ApiEndpointOption endpoint)
    {
        string key = $"{endpoint.Name}-RateLimit-{token}";

        return _cache.GetOrCreate(key, (entry) =>
        {
            entry.SlidingExpiration = endpoint.RateLimitPeriod * 2;

            return Policy
                .RateLimitAsync(endpoint.RateLimit, endpoint.RateLimitPeriod, endpoint.RateLimitBurst)
                .WithPolicyKey($"{endpoint.Name} Rate Limit for {token}");
        });
    }

    private AsyncRetryPolicy GetRetry(ApiEndpointOption endpoint)
    {
        string key = $"{endpoint.Name}-Retry";

        if (!_registry.TryGet(key, out AsyncRetryPolicy policy))
        {
            policy = Policy
                .Handle<ApiException>(CanRetry)
                .Or<TaskCanceledException>()
                .WaitAndRetryAsync(
                    DecorrelatedJitter(endpoint.Retries, endpoint.RetryDelaySeed, endpoint.RetryDelayMaximum),
                    OnRetry);

            policy.WithPolicyKey($"{endpoint.Name} Retry");

            _registry[key] = policy;
        }

        return policy;
    }

    private AsyncTimeoutPolicy GetTimeout(ApiEndpointOption endpoint)
    {
        string key = $"{endpoint.Name}-Timeout";

        if (!_registry.TryGet(key, out AsyncTimeoutPolicy policy))
        {
            policy = Policy.TimeoutAsync(endpoint.Timeout.Add(TimeSpan.FromSeconds(1)), TimeoutStrategy.Pessimistic, OnTimeoutAsync);
            policy.WithPolicyKey($"{endpoint.Name} Timeout");

            _registry[key] = policy;
        }

        return policy;
    }

    private Task OnBulkheadRejectedAsync(Context context)
    {
        _metricsPublisher.Increment($"polly.bulkhead.{context.OperationKey?.ToLowerInvariant()}");

        Log.RejectedByBulkhead(
            _logger,
            context.PolicyWrapKey,
            context.PolicyKey,
            context.OperationKey);

        return Task.CompletedTask;
    }

    private void OnCircuitBreak(Exception exception, TimeSpan duration, Context context)
    {
        _metricsPublisher.Increment($"polly.circuitbreaker.open.{context.OperationKey?.ToLowerInvariant()}");

        Log.CircuitBroken(
            _logger,
            exception,
            context.PolicyWrapKey,
            context.PolicyKey,
            context.OperationKey,
            duration);
    }

    private void OnCircuitReset(Context context)
    {
        _metricsPublisher.Increment($"polly.circuitbreaker.closed.{context.OperationKey?.ToLowerInvariant()}");

        Log.CircuitReset(
            _logger,
            context.PolicyWrapKey,
            context.PolicyKey,
            context.OperationKey);
    }

    private void OnRetry(Exception exception, TimeSpan delay, int retry, Context context)
    {
        _metricsPublisher.Increment($"polly.retry.{context.OperationKey?.ToLowerInvariant()}.{retry.ToString(CultureInfo.InvariantCulture)}");

        Log.Retry(
            _logger,
            exception,
            context.PolicyWrapKey,
            context.PolicyKey,
            context.OperationKey,
            retry,
            delay);
    }

    private Task OnTimeoutAsync(Context context, TimeSpan timeout, Task task)
    {
        _metricsPublisher.Increment($"polly.timeout.{context.OperationKey?.ToLowerInvariant()}");

        Log.Timeout(
            _logger,
            context.PolicyWrapKey,
            context.PolicyKey,
            context.OperationKey,
            timeout);

        // See https://github.com/App-vNext/Polly/wiki/Timeout#pessimistic-timeout-1
        task?.ContinueWith(
            (t) =>
            {
                if (t.IsFaulted)
                {
                    Log.TimeoutWithException(
                        _logger,
                        t.Exception,
                        context.PolicyWrapKey,
                        context.PolicyKey,
                        context.OperationKey,
                        timeout);
                }
                else if (t.IsCanceled)
                {
                    Log.TimeoutWithExceptionAndCanceled(
                        _logger,
                        t.Exception,
                        context.PolicyWrapKey,
                        context.PolicyKey,
                        context.OperationKey,
                        timeout);
                }
            },
            TaskScheduler.Current);

        return Task.CompletedTask;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static partial class Log
    {
        [LoggerMessage(1, LogLevel.Information, "HTTP dependency execution policies reset by configuration update.")]
        public static partial void PoliciesReset(ILogger logger);

        [LoggerMessage(2, LogLevel.Error, "Circuit-breaker for endpoint {Name} has been isolated by the default configuration.")]
        public static partial void CircuitIsolated(ILogger logger, string name);

        [LoggerMessage(3, LogLevel.Error, "{PolicyWrapKey}:{PolicyKey} at {OperationKey} rejected by bulkhead.")]
        public static partial void RejectedByBulkhead(ILogger logger, string policyWrapKey, string policyKey, string operationKey);

        [LoggerMessage(4, LogLevel.Information, "{PolicyWrapKey}:{PolicyKey} at {OperationKey} caused circuit to be reset.")]
        public static partial void CircuitReset(ILogger logger, string policyWrapKey, string policyKey, string operationKey);

        [LoggerMessage(5, LogLevel.Information, "{PolicyWrapKey}:{PolicyKey} at {OperationKey} timed out after {Timeout}.")]
        public static partial void Timeout(ILogger logger, string policyWrapKey, string policyKey, string operationKey, TimeSpan timeout);

        [LoggerMessage(6, LogLevel.Error, "{PolicyWrapKey}:{PolicyKey} at {OperationKey} has broken the circuit for {Duration}.")]
        public static partial void CircuitBroken(ILogger logger, Exception exception, string policyWrapKey, string policyKey, string operationKey, TimeSpan duration);

        [LoggerMessage(7, LogLevel.Warning, "{PolicyWrapKey}:{PolicyKey} at {OperationKey} has caused a retry after attempt {Retry}. Backing-off for {Delay}.")]
        public static partial void Retry(ILogger logger, Exception exception, string policyWrapKey, string policyKey, string operationKey, int retry, TimeSpan delay);

        [LoggerMessage(8, LogLevel.Information, "{PolicyWrapKey}:{PolicyKey} at {OperationKey} execution timed out after {Timeout} with exception.")]
        public static partial void TimeoutWithException(ILogger logger, Exception exception, string policyWrapKey, string policyKey, string operationKey, TimeSpan timeout);

        [LoggerMessage(9, LogLevel.Information, "{PolicyWrapKey}:{PolicyKey} at {OperationKey} execution timed out after {Timeout} and was cancelled.")]
        public static partial void TimeoutWithExceptionAndCanceled(ILogger logger, Exception exception, string policyWrapKey, string policyKey, string operationKey, TimeSpan timeout);
    }
}
