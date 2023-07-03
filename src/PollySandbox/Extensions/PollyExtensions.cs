// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

using System.Net.Sockets;
using Polly;

namespace PollySandbox;

internal static class PollyExtensions
{
    internal static PolicyBuilder OrHttpRequestFault(this PolicyBuilder builder)
    {
        return builder.Or<HttpRequestException>(CannotConnect)
                      .Or<HttpRequestException>(IsHostNotFound);
    }

    internal static PolicyBuilder<TResult> OrHttpRequestFault<TResult>(this PolicyBuilder<TResult> builder)
    {
        return builder.Or<HttpRequestException>(CannotConnect)
                      .Or<HttpRequestException>(IsHostNotFound);
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
}
