// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

using System.Reflection;

namespace PollySandbox;

public static class HttpServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationHttpClientFactory<TVersion>(
        this IServiceCollection services,
        string applicationName)
    {
        return services.AddApplicationHttpClientFactory(applicationName, typeof(TVersion).GetApplicationVersion());
    }

    public static IServiceCollection AddApplicationHttpClientFactory(
        this IServiceCollection services,
        string applicationName,
        string applicationVersion)
    {
        return services.AddApplicationHttpClientFactory((options) =>
        {
            options.ApplicationName = applicationName;
            options.ApplicationVersion = applicationVersion;
        });
    }

    public static IServiceCollection AddApplicationHttpClientFactory(
        this IServiceCollection services,
        Action<ApplicationHttpOptions> configure)
        => services.AddApplicationHttpClientFactory((_, options) => configure(options));

    internal static IServiceCollection AddApplicationHttpClientFactory(
        this IServiceCollection services,
        Action<IServiceProvider, ApplicationHttpOptions> configure)
    {
        return services.AddApplicationHttpClientFactory((serviceProvider) =>
        {
            var accessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
            var applicationHttpOptions = new ApplicationHttpOptions();

            configure(serviceProvider, applicationHttpOptions);

            return applicationHttpOptions;
        });
    }

    private static string GetApplicationVersion(this Type type)
    {
        if (TryGetAssemblyInfoVersion(type, out var version))
        {
            return version;
        }

        return type.Assembly.GetName().Version!.ToString();
    }

    private static bool TryGetAssemblyInfoVersion(Type type, out string version)
    {
        version = type.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (version == null)
        {
            return false;
        }

        int index = version.IndexOf("+", StringComparison.Ordinal);
        if (index > -1 && index < version.Length - 1 && version[(index + 1)..].Length > 7)
        {
            version = version[..(index + 8)];
        }

        return true;
    }
}
