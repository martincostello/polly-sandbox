// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

using Refit;

namespace PollySandbox;

internal static class RefitHttpClientFactoryServiceCollectionExtensions
{
    public static IHttpClientBuilder AddApplicationHttpClient<TClient, TOptions>(
        this IServiceCollection services,
        string name,
        Action<string, TOptions, HttpClient> configureClient,
        Action<IServiceProvider, RefitSettings> configureRefit)
        where TClient : class where TOptions : class
    {
        return services.AddApplicationHttpClient<TClient>(name, (provider, client) =>
        {
            TOptions options = provider.GetRequiredService<TOptions>();
            configureClient(name, options, client);
        }, configureRefit);
    }

    public static IHttpClientBuilder AddApplicationHttpClient<TClient>(
        this IServiceCollection services,
        string name,
        Action<IServiceProvider, HttpClient> configureClient,
        Action<IServiceProvider, RefitSettings> configureRefit)
        where TClient : class
    {
        return services.AddApplicationHttpClient(name, configureClient).AddRefitClient<TClient>(configureRefit);
    }
}
