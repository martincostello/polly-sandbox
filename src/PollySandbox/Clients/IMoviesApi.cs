// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

using Refit;

namespace PollySandbox;

public interface IMoviesApi
{
    [Get("/api/movies")]
    Task<ApiResponse<IList<Movie>>> GetAsync(CancellationToken cancellationToken = default);

    [Get("/api/movies/{id}")]
    Task<ApiResponse<Movie>> GetAsync(string id, CancellationToken cancellationToken = default);
}
