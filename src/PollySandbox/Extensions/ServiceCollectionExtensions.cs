// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using PollySandbox.Extensions;
using Refit;

namespace PollySandbox;

public static class ServiceCollectionExtensions
{
    public static void AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddResilience();

        services.AddHttpContextAccessor();
        services.AddMemoryCache();

        services.AddOptions()
                .Configure<ApiOptions>(configuration.GetSection("Api"))
                .Configure<JsonOptions>((options) => ConfigureJsonFormatter(options.SerializerOptions));

        services.AddSingleton(Random.Shared);
        services.AddSingleton<IMetricsPublisher, MetricsPublisher>();
        services.AddSingleton<ResiliencePipelineFactory>();
        services.AddSingleton<IHttpContentSerializer>((serviceProvider) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<JsonOptions>>();
            return new SystemTextJsonContentSerializer(options.Value.SerializerOptions);
        });

        services.AddApplicationHttpClientFactory<Program>("PollySandbox")
                .AddApplicationHttpClient<IMoviesApi>("Movies")
                .AddApplicationHttpClient<IUsersApi>("Users");

        services.AddScoped((serviceProvider) => serviceProvider.GetRequiredService<IOptionsMonitor<ApiOptions>>().CurrentValue);
        services.AddScoped<IMoviesClient, MoviesClient>();
        services.AddScoped<IUsersClient, UsersClient>();
    }

    private static IServiceCollection AddApplicationHttpClient<TClient>(this IServiceCollection services, string name)
        where TClient : class
    {
        services.AddApplicationHttpClient<TClient, IOptions<ApiOptions>>(name, ConfigureHttpClient, ConfigureRefit);
        return services;
    }

    private static void ConfigureHttpClient(string name, IOptions<ApiOptions> options, HttpClient client)
    {
        ApiEndpointOption endpoint = options.Value.GetEndpoint(name);
        client.BaseAddress = endpoint.Uri;
        client.Timeout = endpoint.Timeout;
    }

    private static void ConfigureRefit(IServiceProvider provider, RefitSettings settings)
    {
        settings.ContentSerializer = provider.GetRequiredService<IHttpContentSerializer>();
    }

    private static void ConfigureJsonFormatter(JsonSerializerOptions options)
    {
        options.PropertyNameCaseInsensitive = false;
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.WriteIndented = true;
    }
}
