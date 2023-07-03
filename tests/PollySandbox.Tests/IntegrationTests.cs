// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using JustEat.HttpClientInterception;
using Microsoft.Extensions.DependencyInjection;

namespace PollySandbox;

[Collection(AppCollection.Name)]
public class IntegrationTests : IAsyncLifetime, IDisposable
{
    private readonly IDisposable _scope;

    public IntegrationTests(AppFixture fixture, ITestOutputHelper outputHelper)
    {
        Fixture = fixture;
        OutputHelper = outputHelper;
        Fixture.SetOutputHelper(OutputHelper);
        _scope = Fixture.Interceptor.BeginScope();
        Fixture.Interceptor.RegisterBundle("bundle.json");
    }

    ~IntegrationTests()
    {
        Dispose(false);
    }

    [Fact]
    public async Task Can_Get_Movie()
    {
        // Arrange
        using var client = Fixture.CreateHttpClientForApp();

        // Act
        var movie = await client.GetFromJsonAsync<Movie>("/movies/5");

        // Assert
        movie.ShouldNotBeNull();
        movie.Name.ShouldBe("Star Wars: Episode V - The Empire Strikes Back");
    }

    [Fact]
    public async Task Can_Get_Movies()
    {
        // Arrange
        using var client = Fixture.CreateHttpClientForApp();

        // Act
        var movies = await client.GetFromJsonAsync<IList<Movie>>("/movies");

        // Assert
        movies.ShouldNotBeNull();
        movies.Count.ShouldBe(3);
        movies[0].Name.ShouldBe("Star Wars: Episode IV - A New Hope");
        movies[1].Name.ShouldBe("Star Wars: Episode V - The Empire Strikes Back");
        movies[2].Name.ShouldBe("Star Wars: Episode VI - Return of the Jedi");
    }

    [Fact]
    public async Task Can_Get_User()
    {
        // Arrange
        using var client = Fixture.CreateHttpClientForApp();

        // Act
        var user = await client.GetFromJsonAsync<User>("/users/2");

        // Assert
        user.ShouldNotBeNull();
        user.Name.ShouldBe("Darth Vader");
    }

    [Fact]
    public async Task Can_Get_Users()
    {
        // Arrange
        using var client = Fixture.CreateHttpClientForApp();

        // Act
        var users = await client.GetFromJsonAsync<IList<User>>("/users");

        // Assert
        users.ShouldNotBeNull();
        users.Count.ShouldBe(2);
        users[0].Name.ShouldBe("Luke Skywalker");
        users[1].Name.ShouldBe("Darth Vader");
    }

    [Fact]
    public async Task Client_Is_Rate_Limited_Per_Client_If_Rate_Limit_Exceeded()
    {
        // Arrange
        Fixture.OverrideConfiguration("Api:Endpoints:Movies:RateLimit", "1");
        Fixture.OverrideConfiguration("Api:Endpoints:Movies:RateLimitBurst", "1");
        Fixture.OverrideConfiguration("Api:Endpoints:Movies:RateLimitPeriod", "00:01:00", reload: true);

        Fixture.Services.GetRequiredService<PolicyStore>().Clear();

        try
        {
            string requestUri = "/movies";

            using var client1 = Fixture.CreateHttpClientForApp();
            using var client2 = Fixture.CreateHttpClientForApp();

            client1.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", "token-1");
            client2.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", "token-2");

            // Act
            using var response200 = await client1.GetAsync(requestUri);

            // Assert
            response200.StatusCode.ShouldBe(HttpStatusCode.OK);

            // Act
            using var response429 = await client1.GetAsync(requestUri);

            // Assert
            response429.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

            // Act
            using var otherResponse200 = await client2.GetAsync(requestUri);

            // Assert
            otherResponse200.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            Fixture.Services.GetRequiredService<PolicyStore>().Clear();
        }
    }

    protected AppFixture Fixture { get; }

    protected ITestOutputHelper OutputHelper { get; }

    public virtual Task InitializeAsync() => Task.CompletedTask;

    public virtual Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        _scope?.Dispose();
        Fixture.ClearConfigurationOverrides();
    }
}
