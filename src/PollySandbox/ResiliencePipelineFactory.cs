// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

using Polly;
using Polly.CircuitBreaker;
using Polly.Fallback;
using Polly.Registry;
using Polly.Timeout;
using Refit;

namespace PollySandbox;

public partial class ResiliencePipelineFactory
{
    private readonly ResiliencePipelineRegistry<string> _registry;

    public ResiliencePipelineFactory(ResiliencePipelineRegistry<string> registry)
    {
        _registry = registry;
    }

    public ResiliencePipeline GetPipeline(ApiEndpointOption endpoint, string resource)
        => _registry.GetPipeline($"{endpoint.Name}/{resource}");

    public ResiliencePipeline<TResult> GetPipeline<TResult>(
        ApiEndpointOption endpoint,
        bool handleExecutionFaults,
        string resource)
    {
        return _registry.GetOrAddPipeline<TResult>($"{endpoint.Name}/Fallback/{resource}/{handleExecutionFaults}", (builder) =>
        {
            var shouldHandle = new PredicateBuilder<TResult>()
                .Handle<ApiException>()
                .HandleHttpRequestFault()
                .Handle<TaskCanceledException>();

            if (handleExecutionFaults)
            {
                shouldHandle = shouldHandle
                    .Handle<BrokenCircuitException>()
                    .Handle<IsolatedCircuitException>()
                    .Handle<TimeoutRejectedException>();
            }

            builder
                .AddPipeline(GetPipeline(endpoint, resource))
                .AddFallback(new FallbackStrategyOptions<TResult>()
                {
                    FallbackAction = (context) =>
                    {
                        if (context.Context.GetFallbackGenerator<TResult>() is { } generator)
                        {
                            return Outcome.FromResultAsTask(generator());
                        }

                        return Outcome.FromResultAsTask<TResult>(default);
                    },
                    Name = $"{endpoint.Name} Fallback",
                    ShouldHandle = shouldHandle,
                });
        });
    }
}
