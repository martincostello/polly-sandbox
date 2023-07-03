// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

using Microsoft.Extensions.Http;

namespace PollySandbox;

internal sealed class OptionsMessageHandlerBuilderFilter : IHttpMessageHandlerBuilderFilter
{
    private readonly IServiceProvider _serviceProvider;

    public OptionsMessageHandlerBuilderFilter(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
    {
        return (builder) =>
        {
            next(builder);
            _serviceProvider.GetRequiredService<ApplicationHttpOptions>().OnConfigureHandlers?.Invoke(_serviceProvider, builder);
        };
    }
}
