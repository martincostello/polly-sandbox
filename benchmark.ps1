#! /usr/bin/env pwsh

#Requires -PSEdition Core
#Requires -Version 7

dotnet run --configuration Release --project "./perf/PollySandbox.Benchmarks/PollySandbox.Benchmarks.csproj"
