// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

namespace PollySandbox;

public class UsersClient : ApiClient, IUsersClient
{
    private readonly IUsersApi _client;

    public UsersClient(
        IUsersApi client,
        IHttpContextAccessor httpContextAccessor,
        ApiOptions options,
        PolicyFactory policyFactory,
        ILogger<MoviesClient> logger)
        : base(httpContextAccessor, options, policyFactory, logger)
    {
        _client = client;
    }

    protected override ApiEndpointOption EndpointOption => Options.UsersEndpoint();

    protected override string OperationPrefix => "Users";

    public async Task<IList<User>> GetUsersAsync(CancellationToken cancellationToken)
    {
        return await ExecuteAsync(
            GetTokenForRateLimit(HttpContextAccessor.HttpContext.Request.Headers.UserAgent),
            nameof(GetUsersAsync),
            _client.GetAsync,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<User> GetUserAsync(string id, CancellationToken cancellationToken)
    {
        var executionOptions = new ClientExecuteOptions<User>
        {
            HandleExecutionFaults = true,
        };

        return await ExecuteAsync(
            GetTokenForRateLimit(id),
            nameof(GetUserAsync),
            token => _client.GetAsync(id, token),
            executionOptions,
            cancellationToken).ConfigureAwait(false);
    }
}
