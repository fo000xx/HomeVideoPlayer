# Video ingestion integration tests

The integration tests use real FFmpeg and ExifTool processes, isolated temporary directories, and a fresh SQLite database per test. The fixture applies the Web API project's EF Core migrations before running each scenario. Because ExifTool does not support writing MTS files, a test-only wrapper runs the real ExifTool to read the generated media and adds a synthetic `DateTimeOriginal` value to its JSON output using `jq`.

Run the tests directly on a development machine with .NET 10, FFmpeg, ExifTool, and `jq` installed:

```sh
dotnet test HomeVideoPlayer.WebApi.Tests/HomeVideoPlayer.WebApi.Tests.csproj
```