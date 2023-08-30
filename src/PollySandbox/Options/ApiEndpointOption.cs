// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

namespace PollySandbox;

public sealed class ApiEndpointOption
{
    public string Name { get; set; }

    public Uri Uri { get; set; }

    public TimeSpan Timeout { get; set; }

    public double FailureThreshold { get; set; } = 0.5;

    public TimeSpan FailureSamplingDuration { get; set; } = TimeSpan.FromSeconds(10);

    public int FailureMinimumThroughput { get; set; } = 20;

    public TimeSpan FailureBreakDuration { get; set; } = TimeSpan.FromSeconds(30);

    public int Retries { get; set; }

    public TimeSpan RetryDelaySeed { get; set; } = TimeSpan.FromSeconds(0.1);

    public TimeSpan RetryDelayMaximum { get; set; } = TimeSpan.FromSeconds(0.5);

    public bool Isolate { get; set; }

    public int RateLimit { get; set; }

    public TimeSpan RateLimitPeriod { get; set; } = TimeSpan.FromSeconds(5);
}
