// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

namespace PollySandbox;

public interface IMoviesClient
{
    Task<Movie> GetMovieAsync(string id, CancellationToken cancellationToken);

    Task<IList<Movie>> GetMoviesAsync(CancellationToken cancellationToken);
}
