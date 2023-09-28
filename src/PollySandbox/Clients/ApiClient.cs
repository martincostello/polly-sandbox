// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

using System.Net;
using Polly;
using Refit;

namespace PollySandbox;

public abstract class ApiClient
{
    protected ApiClient(
        IHttpContextAccessor httpContextAccessor,
        ApiOptions options,
        ResiliencePipelineFactory pipelineFactory,
        ILogger logger)
    {
        HttpContextAccessor = httpContextAccessor;
        Logger = logger;
        Options = options;
        PipelineFactory = pipelineFactory;
    }

    protected abstract ApiEndpointOption EndpointOption { get; }

    protected IHttpContextAccessor HttpContextAccessor { get; }

    protected ILogger Logger { get; }

    protected abstract string OperationPrefix { get; }

    protected ApiOptions Options { get; }

    protected ResiliencePipelineFactory PipelineFactory { get; }

    protected async Task<T> ExecuteAsync<T>(
        string rateLimitToken,
        string operationKey,
        Func<CancellationToken, Task<ApiResponse<T>>> func,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(rateLimitToken, operationKey, func, null, cancellationToken);
    }

    protected async Task<T> ExecuteAsync<T>(
        string rateLimitToken,
        string operationKey,
        Func<CancellationToken, Task<ApiResponse<T>>> func,
        ClientExecuteOptions<T> clientExecuteOptions,
        CancellationToken cancellationToken)
    {
        using (Logger.BeginScope(operationKey))
        {
            return await ExecuteAsync(
                rateLimitToken,
                operationKey,
                clientExecuteOptions,
                async (context, token) =>
                {
                    using ApiResponse<T> response = await func(token).ConfigureAwait(false);

                    if (response.StatusCode == HttpStatusCode.NotFound && clientExecuteOptions?.ThrowIfNotFound != true)
                    {
                        return default;
                    }

                    if (response.StatusCode == HttpStatusCode.BadRequest && clientExecuteOptions?.OnBadRequest != null)
                    {
                        clientExecuteOptions.OnBadRequest();
                        return default;
                    }

                    if (response.Error != null)
                    {
                        throw response.Error;
                    }

                    await response.EnsureSuccessStatusCodeAsync();

                    return response.Content;
                },
                cancellationToken).ConfigureAwait(false);
        }
    }

    protected string GetTokenForRateLimit(string fallbackToken)
    {
        var authorization = HttpContextAccessor.HttpContext?.Request.Headers.Authorization;
        return
            !string.IsNullOrEmpty(authorization) ?
            authorization :
            fallbackToken ?? string.Empty;
    }

    private async Task<TResult> ExecuteAsync<TResult>(
        string rateLimitToken,
        string operationKey,
        ClientExecuteOptions<TResult> clientExecuteOptions,
        Func<ResilienceContext, CancellationToken, ValueTask<TResult>> action,
        CancellationToken cancellationToken)
    {
        var context = ResilienceContextPool.Shared.Get($"{OperationPrefix}.{operationKey}", cancellationToken);

        try
        {
            context.SetRateLimitPartition(rateLimitToken);

            if (clientExecuteOptions?.HandleExecutionFaults.HasValue is true)
            {
                context.SetFallbackGenerator(clientExecuteOptions.FallbackValue);

                var pipeline = PipelineFactory.GetPipeline<TResult>(
                    EndpointOption,
                    clientExecuteOptions.HandleExecutionFaults.GetValueOrDefault(),
                    operationKey);

                return await pipeline.ExecuteAsync(
                    async (context) => await action(context, context.CancellationToken),
                    context).ConfigureAwait(false);
            }
            else
            {
                var pipeline = PipelineFactory.GetPipeline(EndpointOption, operationKey);

                return await pipeline.ExecuteAsync(
                    async (context) => await action(context, context.CancellationToken),
                    context).ConfigureAwait(false);
            }
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }
}
