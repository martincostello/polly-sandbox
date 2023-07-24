// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

using Microsoft.Extensions.Caching.Memory;
using Polly;
using Polly.RateLimit;
using PollySandbox;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationServices(builder.Configuration);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.MapGet("/movies", async (IMoviesClient client, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await client.GetMoviesAsync(cancellationToken));
    }
    catch (RateLimitRejectedException)
    {
        return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    }
    catch (ExecutionRejectedException)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/movies/{id}", async (string id, IMoviesClient client, CancellationToken cancellationToken) =>
{
    try
    {
        var movie = await client.GetMovieAsync(id, cancellationToken);
        return movie is null ? Results.NotFound() : Results.Ok(movie);
    }
    catch (RateLimitRejectedException)
    {
        return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    }
    catch (ExecutionRejectedException)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/users", async (IUsersClient client, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await client.GetUsersAsync(cancellationToken));
    }
    catch (RateLimitRejectedException)
    {
        return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    }
    catch (ExecutionRejectedException)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/users/{id}", async (string id, IUsersClient client, CancellationToken cancellationToken) =>
{
    try
    {
        var user = await client.GetUserAsync(id, cancellationToken);
        return user is null ? Results.NotFound() : Results.Ok(user);
    }
    catch (RateLimitRejectedException)
    {
        return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    }
    catch (ExecutionRejectedException)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/reload", (IConfiguration config, PolicyStore policyStore, IMemoryCache cache) =>
{
    if (config is IConfigurationRoot root)
    {
        root.Reload();
    }

    policyStore?.Clear();
    if (cache is MemoryCache memoryCache)
    {
        memoryCache.Compact(100);
    }

    return Results.Ok();
});

app.Run();

namespace PollySandbox
{
    public partial class Program
    {
    }
}
