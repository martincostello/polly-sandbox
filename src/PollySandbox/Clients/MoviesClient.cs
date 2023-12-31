﻿// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

namespace PollySandbox;

public class MoviesClient : ApiClient, IMoviesClient
{
    private readonly IMoviesApi _client;

    public MoviesClient(
        IMoviesApi client,
        IHttpContextAccessor httpContextAccessor,
        ApiOptions options,
        ResiliencePipelineFactory pipelineFactory,
        ILogger<MoviesClient> logger)
        : base(httpContextAccessor, options, pipelineFactory, logger)
    {
        _client = client;
    }

    protected override ApiEndpointOption EndpointOption => Options.MoviesEndpoint();

    protected override string OperationPrefix => "Movies";

    public async Task<IList<Movie>> GetMoviesAsync(CancellationToken cancellationToken)
    {
        return await ExecuteAsync(
            GetTokenForRateLimit(HttpContextAccessor.HttpContext?.Request.Headers.UserAgent),
            nameof(GetMoviesAsync),
            _client.GetAsync,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Movie> GetMovieAsync(string id, CancellationToken cancellationToken)
    {
        var executionOptions = new ClientExecuteOptions<Movie>
        {
            HandleExecutionFaults = true,
        };

        return await ExecuteAsync(
            GetTokenForRateLimit(id),
            nameof(GetMovieAsync),
            token => _client.GetAsync(id, token),
            executionOptions,
            cancellationToken).ConfigureAwait(false);
    }
}
