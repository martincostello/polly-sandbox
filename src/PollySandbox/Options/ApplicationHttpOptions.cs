// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

using Microsoft.Extensions.Http;

namespace PollySandbox;

public sealed class ApplicationHttpOptions
{
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(5.0);


    public TimeSpan DefaultConnectTimeout { get; set; } = TimeSpan.FromSeconds(5.0);


    public string ApplicationName { get; set; } = string.Empty;


    public string ApplicationVersion { get; set; } = string.Empty;


    public Func<string, string> HttpRequestHeader { get; set; } = static (_) => null;


    public Action<string, IEnumerable<string>> WriteHttpResponseHeader { get; set; } = static (_, _) => { };


    public Func<IEnumerable<string>> HttpRequestHeaderKeys { get; set; } = Array.Empty<string>;


    public Func<string> TraceIdentifier { get; set; } = Guid.NewGuid().ToString;


    public Action<IServiceProvider, HttpMessageHandlerBuilder> OnConfigureHandlers { get; set; }


    public Action<ApplicationHttpOptions, HttpRequestMessage> OnAppendHeaders { get; set; }
}
