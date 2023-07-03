// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http.Headers;

namespace PollySandbox;

internal static class IHttpClientBuilderExtensions
{
    public static IHttpClientBuilder ApplyDefaultConfiguration(this IHttpClientBuilder builder)
        => builder.ConfigurePrimaryHttpMessageHandler(CreatePrimaryHttpHandler)
                  .ConfigureHttpClient(ApplyDefaultConfiguration)
                  .SetHandlerLifetime(TimeSpan.FromMinutes(1.0));

    private static void ApplyDefaultConfiguration(IServiceProvider serviceProvider, HttpClient client)
    {
        var options = serviceProvider.GetRequiredService<ApplicationHttpOptions>();
        var userAgent = new ProductInfoHeaderValue(options.ApplicationName, options.ApplicationVersion);

        client.DefaultRequestHeaders.UserAgent.Add(userAgent);
        client.Timeout = options.DefaultTimeout;
    }

    private static HttpMessageHandler CreatePrimaryHttpHandler(IServiceProvider serviceProvider)
    {
        var automaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        var options = serviceProvider.GetRequiredService<ApplicationHttpOptions>();

        return new SocketsHttpHandler
        {
            AutomaticDecompression = automaticDecompression,
            ConnectTimeout = options.DefaultConnectTimeout
        };
    }
}
