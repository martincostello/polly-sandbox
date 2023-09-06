// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.RateLimiting;
using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Registry;
using Polly.Telemetry;
using Polly.Timeout;
using Refit;

namespace PollySandbox.Extensions;

public static partial class ResilienceExtensions
{
    public static IServiceCollection AddResilience(this IServiceCollection services)
    {
        return AddResilience(services, new Dictionary<string, IList<string>>
        {
            ["Movies"] = GetEndpointNames<MoviesClient>(),
            ["Users"] = GetEndpointNames<UsersClient>(),
        });
    }

    public static IServiceCollection AddResilience(this IServiceCollection services, IDictionary<string, IList<string>> endpoints)
    {
        services.AddSingleton<MetricsTelemetryListener>();

        services.AddOptions<TelemetryOptions>()
                .Configure<MetricsTelemetryListener>(
                    (options, listener)
                        => options.TelemetryListeners.Add(listener));

        foreach ((var name, var resources) in endpoints)
        {
            var rateLimitPipeline = $"{name}/RateLimit";
            var retryPipeline = $"{name}/Retry";
            var timeoutPipeline = $"{name}/Timeout";

            services.AddResiliencePipeline(rateLimitPipeline, name, ApplyRateLimits);
            services.AddResiliencePipeline(retryPipeline, name, ApplyRetries);
            services.AddResiliencePipeline(timeoutPipeline, name, ApplyTimeout);

            foreach (var resource in resources)
            {
                var circuitBreakerPipeline = $"{name}/CircuitBreaker/{resource}";

                services.AddResiliencePipeline(circuitBreakerPipeline, name, ApplyCircuitBreaker);

                services.AddResiliencePipeline($"{name}/{resource}", (builder, context) =>
                {
                    builder.InstanceName = name;
                    context.EnableReloads<ApiOptions>();

                    var registry = context.ServiceProvider.GetRequiredService<ResiliencePipelineRegistry<string>>();
                    var pipelines = new[]
                    {
                        rateLimitPipeline,
                        timeoutPipeline,
                        circuitBreakerPipeline,
                        retryPipeline,
                    };

                    foreach (var pipeline in pipelines)
                    {
                        builder.AddPipeline(registry.GetPipeline(pipeline));
                    }
                });
            }
        }

        return services;
    }

    private static void AddResiliencePipeline(
        this IServiceCollection services,
        string key,
        string name,
        Action<ResiliencePipelineBuilder, ApiEndpointOption> configure)
    {
        services.AddResiliencePipeline(key, (builder, context) =>
        {
            builder.InstanceName = name;
            context.EnableReloads<ApiOptions>();
            configure(builder, context.GetOptions<ApiOptions>().GetEndpoint(name));
        });
    }

    private static void ApplyCircuitBreaker(ResiliencePipelineBuilder builder, ApiEndpointOption endpoint)
    {
        var manualControl = new CircuitBreakerManualControl(endpoint.Isolate);

        builder.AddCircuitBreaker(new()
        {
            BreakDuration = endpoint.FailureBreakDuration,
            FailureRatio = endpoint.FailureThreshold,
            ManualControl = manualControl,
            MinimumThroughput = endpoint.FailureMinimumThroughput,
            Name = builder.Name,
            SamplingDuration = endpoint.FailureSamplingDuration,
            ShouldHandle = new PredicateBuilder()
                .Handle<ApiException>(CanCircuitBreak)
                .HandleHttpRequestFault()
                .Handle<OperationCanceledException>()
                .Handle<TimeoutRejectedException>(),
        });
    }

    private static void ApplyRateLimits(ResiliencePipelineBuilder builder, ApiEndpointOption endpoint)
    {
        if (endpoint.RateLimit > 0)
        {
            var rateLimiter = PartitionedRateLimiter.Create<ResilienceContext, string>((context) =>
            {
                return RateLimitPartition.GetTokenBucketLimiter(
                    context.GetRateLimitPartition(),
                    (_) => new()
                    {
                        ReplenishmentPeriod = endpoint.RateLimitPeriod,
                        TokenLimit = endpoint.RateLimit,
                        TokensPerPeriod = endpoint.RateLimit,
                    });
            });

            builder.AddRateLimiter(new RateLimiterStrategyOptions()
            {
                Name = builder.Name,
                RateLimiter = (context) => rateLimiter.AcquireAsync(context.Context),
            });
        }
    }

    private static void ApplyRetries(ResiliencePipelineBuilder builder, ApiEndpointOption endpoint)
    {
        if (endpoint.Retries > 0)
        {
            builder.AddRetry(new()
            {
                BackoffType = DelayBackoffType.Constant,
                Delay = endpoint.RetryDelaySeed,
                MaxRetryAttempts = endpoint.Retries,
                Name = builder.Name,
                ShouldHandle = new PredicateBuilder().Handle<ApiException>(CanRetry).Handle<TaskCanceledException>(),
                UseJitter = true,
            });
        }
    }

    private static void ApplyTimeout(ResiliencePipelineBuilder builder, ApiEndpointOption endpoint)
    {
        builder.AddTimeout(new TimeoutStrategyOptions()
        {
            Name = builder.Name,
            Timeout = endpoint.Timeout.Add(TimeSpan.FromSeconds(1)),
        });
    }

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

    private static string[] GetEndpointNames<T>()
        => typeof(T).GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public)
                    .Select((p) => p.Name)
                    .ToArray();

    private sealed class MetricsTelemetryListener : TelemetryListener
    {
        public MetricsTelemetryListener(IMetricsPublisher publisher)
        {
            Publisher = publisher;
        }

        private IMetricsPublisher Publisher { get; }

        public override void Write<TResult, TArgs>(in TelemetryEventArguments<TResult, TArgs> args)
        {
            var bucket = new StringBuilder("polly.")
                .Append(args.Source.StrategyName)
                .Append('.')
                .Append(args.Event.EventName);

            if (args.Context.OperationKey is { Length: > 0 } key)
            {
                bucket.Append('.')
                      .Append(key);
            }

            Publisher.Increment(bucket.ToString());
        }
    }
}
