// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

using Microsoft.Extensions.Http;

namespace PollySandbox;

internal static class HttpClientFactoryServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationHttpClientFactory(
        this IServiceCollection services,
        Func<IServiceProvider, ApplicationHttpOptions> optionsFactory)
    {
        services.AddSingleton(optionsFactory);
        services.AddSingleton<IHttpMessageHandlerBuilderFilter, OptionsMessageHandlerBuilderFilter>();
        services.AddHttpClient();
        services.AddApplicationHttpClient(Microsoft.Extensions.Options.Options.DefaultName);
        services.AddTransient((serviceProvider) => serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient());

        return services;
    }

    public static IHttpClientBuilder AddApplicationHttpClient(this IServiceCollection services, string name)
        => services.AddApplicationHttpClient(name, (Action<IServiceProvider, HttpClient>)null);

    public static IHttpClientBuilder AddApplicationHttpClient(
        this IServiceCollection services,
        string name,
        Action<IServiceProvider, HttpClient> configureClient)
        => services.AddApplicationHttpClient(name, (_, provider, client) => configureClient?.Invoke(provider, client));

    public static IHttpClientBuilder AddApplicationHttpClient(
        this IServiceCollection services,
        string name,
        Action<string, IServiceProvider, HttpClient> configureClient)
    {
        var httpClientBuilder = services.AddHttpClient(name).ApplyDefaultConfiguration();

        if (configureClient != null)
        {
            httpClientBuilder.ConfigureHttpClient((provider, client) => configureClient(name, provider, client));
        }

        return httpClientBuilder;
    }
}
