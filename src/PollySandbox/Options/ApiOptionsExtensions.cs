// Copyright (c) Martin Costello, 2023. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

namespace PollySandbox;

public static class ApiOptionsExtensions
{
    public static ApiEndpointOption MoviesEndpoint(this ApiOptions options)
    {
        return options.GetEndpoint("Movies");
    }

    public static ApiEndpointOption UsersEndpoint(this ApiOptions options)
    {
        return options.GetEndpoint("Users");
    }

    public static ApiEndpointOption GetEndpoint(this ApiOptions options, string name)
    {
        if (!options.Endpoints.TryGetValue(name, out var option))
        {
            option = null;
        }

        return option;
    }
}
