// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

using Refit;

namespace PollySandbox;

public interface IUsersApi
{
    [Get("/api/users")]
    Task<ApiResponse<IList<User>>> GetAsync(CancellationToken cancellationToken = default);

    [Get("/api/users/{id}")]
    Task<ApiResponse<User>> GetAsync(string id, CancellationToken cancellationToken = default);
}
