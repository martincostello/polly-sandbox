// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

using System.Net.Sockets;
using Polly;

namespace PollySandbox;

internal static class PollyExtensions
{
    private static readonly ResiliencePropertyKey<string> PartitionKey = new ("RateLimitPartition");

    internal static Func<TResult> GetFallbackGenerator<TResult>(this ResilienceContext context)
    {
        if (!context.Properties.TryGetValue(FallbackKeys<TResult>.FallbackGenerator, out var fallbackGenerator))
        {
            fallbackGenerator = null;
        }

        return fallbackGenerator;
    }

    internal static string GetRateLimitPartition(this ResilienceContext context)
    {
        if (!context.Properties.TryGetValue(PartitionKey, out var partitionKey))
        {
            throw new InvalidOperationException("No rate limit partition key was specified at the resilience pipeline callsite.");
        }

        return partitionKey;
    }

    internal static void SetFallbackGenerator<TResult>(this ResilienceContext context, Func<TResult> value)
        => context.Properties.Set(FallbackKeys<TResult>.FallbackGenerator, value);

    internal static void SetRateLimitPartition(this ResilienceContext context, string value)
        => context.Properties.Set(PartitionKey, value);


    internal static PredicateBuilder<TResult> HandleHttpRequestFault<TResult>(this PredicateBuilder<TResult> builder)
    {
        return builder.Handle<HttpRequestException>(CannotConnect)
                      .Handle<HttpRequestException>(IsHostNotFound);
    }

    private static bool CannotConnect(HttpRequestException exception)
    {
        if (exception.InnerException is SocketException socketException)
        {
            return socketException.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => true,
                SocketError.NoData => true,
                _ => false,
            };
        }

        if (exception.InnerException is IOException io)
        {
            return string.Equals(io.Message, "The response ended prematurely.", System.StringComparison.Ordinal);
        }

        return false;
    }

    private static bool IsHostNotFound(HttpRequestException exception)
    {
        const int HostNotFoundHResult = -2147012889;
        const int HostNotFoundErrorCode = 12007;

        if (exception.HResult == HostNotFoundHResult)
        {
            return true;
        }

        if (exception.InnerException is SocketException socketException)
        {
            return socketException.SocketErrorCode == SocketError.HostNotFound;
        }
        else if (exception.InnerException is System.ComponentModel.Win32Exception winException)
        {
            return winException.NativeErrorCode == HostNotFoundErrorCode;
        }

        return false;
    }

    private static class FallbackKeys<T>
    {
        public static readonly ResiliencePropertyKey<Func<T>> FallbackGenerator = new("FallbackGenerator");
    }
}
