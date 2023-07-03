// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

using Refit;

namespace PollySandbox;

public static class RefitHttpClientBuilderExtensions
{
    public static IHttpClientBuilder AddRefitClient<TClient>(this IHttpClientBuilder builder, Action<IServiceProvider, RefitSettings> configureRefit)
        where TClient : class
        => builder.AddTypedClient((client, provider) => CreateRefitClient<TClient>(client, provider, configureRefit));

    private static TClient CreateRefitClient<TClient>(
        HttpClient client,
        IServiceProvider provider,
        Action<IServiceProvider, RefitSettings> configureRefit)
    {
        var refitSettings = new RefitSettings
        {
            HttpMessageHandlerFactory = () => CreateMessageHandler<TClient>(provider)
        };

        if (provider.GetService<IHttpContentSerializer>() is { } serializer)
        {
            refitSettings.ContentSerializer = serializer;
        }

        configureRefit?.Invoke(provider, refitSettings);

        return RestService.For<TClient>(client, refitSettings);
    }

    private static HttpMessageHandler CreateMessageHandler<TClient>(IServiceProvider provider)
        => provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(typeof(TClient).Name);
}
