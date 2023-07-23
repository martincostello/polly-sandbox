// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

using System.Reflection;
using JustEat.HttpClientInterception;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace PollySandbox;

public class AppServer : WebApplicationFactory<Program>
{
    private readonly HttpClientInterceptorOptions _interceptor = new();

    public AppServer()
    {
        ClientOptions.AllowAutoRedirect = false;
        ClientOptions.BaseAddress = new("https://localhost");

        _interceptor.ThrowsOnMissingRegistration()
                    .RegisterBundle(typeof(AppServer).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(p => p.Key is "HttpBundlePath").Value);
    }

    public virtual Uri ServerUri => ClientOptions.BaseAddress;

    public virtual HttpClient CreateHttpClientForApp()
        => CreateDefaultClient();

    protected override void ConfigureClient(HttpClient client)
    {
        client.BaseAddress = ServerUri;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(builder =>
               {
                   builder.AddInMemoryCollection(new[]
                   {
                       KeyValuePair.Create("Api:Endpoints:Movies:RateLimit", "1000000"),
                       KeyValuePair.Create("Api:Endpoints:Users:RateLimit", "1000000"),
                   });
               })
               .ConfigureLogging(builder => builder.ClearProviders())
               .ConfigureServices(services => services.AddSingleton<IHttpMessageHandlerBuilderFilter, HttpClientInterceptionFilter>((_) => new HttpClientInterceptionFilter(_interceptor)))
               .UseEnvironment(Environments.Production);
    }

    private sealed class HttpClientInterceptionFilter : IHttpMessageHandlerBuilderFilter
    {
        private readonly HttpClientInterceptorOptions _options;

        public HttpClientInterceptionFilter(HttpClientInterceptorOptions options)
        {
            _options = options;
        }

        /// <inheritdoc/>
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
        {
            return (builder) =>
            {
                next(builder);
                builder.AdditionalHandlers.Add(_options.CreateHttpMessageHandler());
            };
        }
    }
}
