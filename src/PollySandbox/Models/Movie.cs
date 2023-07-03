// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

using System.Text.Json.Serialization;

namespace PollySandbox;

public sealed class Movie
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("movie")]
    public string Name { get; set; }

    [JsonPropertyName("rating")]
    public float Rating { get; set; }

    [JsonPropertyName("image")]
    public string ImageUrl { get; set; }

    [JsonPropertyName("imdb_url")]
    public string ImdbUrl { get; set; }
}
