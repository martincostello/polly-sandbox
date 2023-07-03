// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

namespace PollySandbox;

public sealed class ClientExecuteOptions<T>
{
    public Func<T> FallbackValue { get; set; }

    public bool? HandleExecutionFaults { get; set; }

    public Action OnBadRequest { get; set; }

    public bool? ThrowIfNotFound { get; set; }
}
