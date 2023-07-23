// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

using System.Net.Http.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;

namespace PollySandbox.Benchmarks;

[EventPipeProfiler(EventPipeProfile.CpuSampling)]
[MemoryDiagnoser]
public class AppBenchmarks
{
    private AppServer _service;
    private HttpClient _client;

    [GlobalSetup]
    public void StartServer()
    {
        _service = new AppServer();
        _client = _service.CreateDefaultClient();
    }

    [GlobalCleanup]
    public async Task StopServer()
        => await _service.DisposeAsync();

    [Benchmark]
    public async Task<Movie[]> GetMovies()
        => await _client.GetFromJsonAsync<Movie[]>("/movies");

    [Benchmark]
    public async Task<Movie> GetMovie()
        => await _client.GetFromJsonAsync<Movie>("/movies/1");

    [Benchmark]
    public async Task<User[]> GetUsers()
        => await _client.GetFromJsonAsync<User[]>("/users");

    [Benchmark]
    public async Task<User> GetUser()
        => await _client.GetFromJsonAsync<User>("/users/1");
}
