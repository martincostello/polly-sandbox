// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using Microsoft.Extensions.DependencyInjection;

namespace PollySandbox.Benchmarks;

[EventPipeProfiler(EventPipeProfile.CpuSampling)]
[MemoryDiagnoser]
public class MicroBenchmarks
{
    private AppServer _service;
    private IMoviesClient _client;

    [GlobalSetup]
    public void StartServer()
    {
        _service = new AppServer();
        using (_service.CreateDefaultClient())
        {
        }

        _client = _service.Services.GetRequiredService<IMoviesClient>();
    }

    [GlobalCleanup]
    public async Task StopServer()
        => await _service.DisposeAsync();

    [Benchmark]
    public async Task<IList<Movie>> GetMovies()
        => await _client.GetMoviesAsync(default);

    [Benchmark]
    public async Task<Movie> GetMovie()
        => await _client.GetMovieAsync("1", default);
}
