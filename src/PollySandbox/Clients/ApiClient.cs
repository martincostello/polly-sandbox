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
        PolicyFactory policyFactory,
        ILogger logger)
    {
        HttpContextAccessor = httpContextAccessor;
        Logger = logger;
        Options = options;
        PolicyFactory = policyFactory;
    }

    protected abstract ApiEndpointOption EndpointOption { get; }

    protected IHttpContextAccessor HttpContextAccessor { get; }

    protected ILogger Logger { get; }

    protected abstract string OperationPrefix { get; }

    protected ApiOptions Options { get; }

    protected PolicyFactory PolicyFactory { get; }

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
        Func<Context, CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken)
    {
        var context = new Context($"{OperationPrefix}.{operationKey}");

        if (clientExecuteOptions?.HandleExecutionFaults.HasValue == true)
        {
            IAsyncPolicy<TResult> policy = GetPolicy(
                rateLimitToken,
                clientExecuteOptions.FallbackValue,
                clientExecuteOptions.HandleExecutionFaults.Value,
                operationKey);

            return await policy.ExecuteAsync(
                async (context, token) => await action(context, token),
                context,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            IAsyncPolicy policy = GetPolicy(rateLimitToken, operationKey);

            return await policy.ExecuteAsync(
                async (context, token) => await action(context, token),
                context,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private IAsyncPolicy<T> GetPolicy<T>(string rateLimitToken, Func<T> fallbackValue, bool handleExecutionFaults, string resource)
        => PolicyFactory.GetPolicy(rateLimitToken, EndpointOption, fallbackValue, handleExecutionFaults, resource);

    private IAsyncPolicy GetPolicy(string rateLimitToken, string resource)
        => PolicyFactory.GetPolicy(rateLimitToken, EndpointOption, resource);
}
