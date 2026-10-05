using System.Diagnostics;
using System.Reflection;
using HomeVideoPlayer.WebApi.Data;
using HomeVideoPlayer.WebApi.Features.VideoIngestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HomeVideoPlayer.WebApi.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class VideoIngestionIntegrationCollection
{
    public const string Name = "Video ingestion integration tests";
}

[Collection(VideoIngestionIntegrationCollection.Name)]
public sealed class VideoIngestionIntegrationTests
{
    [Fact]
    public async Task ValidMedia_IsConvertedStoredAndRecorded()
    {
        await using var fixture = await IngestionFixture.CreateAsync();
        var inputPath = await fixture.CreateMediaInputAsync("success.MTS");

        await fixture.RunSinglePollAsync();

        Assert.False(File.Exists(inputPath));
        Assert.False(File.Exists(Path.ChangeExtension(inputPath, ".mp4")));

        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var video = Assert.Single(await dbContext.Videos.ToListAsync());

        Assert.Equal("success.mp4", video.VideoName);
        Assert.Equal("success.mp4", video.StoragePath);
        Assert.Equal("success.MTS", video.BackupPath);
        Assert.True(video.VideoDate > DateTime.MinValue);
        Assert.True(File.Exists(Path.Combine(fixture.LongTermDirectory, video.StoragePath!)));
        Assert.True(File.Exists(Path.Combine(fixture.BackupDirectory, video.BackupPath!)));
    }

    [Fact]
    public async Task MissingDateTimeOriginal_RetriesThenQuarantines()
    {
        await using var fixture = await IngestionFixture.CreateAsync();
        var inputPath = await fixture.CreateMediaInputAsync("missing-date.MTS");
        fixture.OmitCaptureDate();

        await fixture.RunSinglePollAsync();

        Assert.True(File.Exists(inputPath));
        Assert.Empty(Directory.GetFiles(fixture.BackupDirectory));
        Assert.Empty(Directory.GetFiles(fixture.LongTermDirectory));

        await fixture.RunSinglePollAsync();

        Assert.False(File.Exists(inputPath));
        Assert.True(File.Exists(Path.Combine(fixture.QuarantineDirectory, "missing-date.MTS")));
        Assert.Empty(Directory.GetFiles(fixture.BackupDirectory));
        Assert.Empty(Directory.GetFiles(fixture.LongTermDirectory));
    }

    [Fact]
    public async Task FfmpegFailure_RetainsInputForRetry_ThenQuarantines()
    {
        await using var fixture = await IngestionFixture.CreateAsync();
        var inputPath = await fixture.CreateMediaInputAsync("bad-output.MTS");
        Directory.CreateDirectory(Path.ChangeExtension(inputPath, ".mp4"));

        await fixture.RunSinglePollAsync();

        Assert.True(File.Exists(inputPath));
        Assert.Empty(Directory.GetFiles(fixture.BackupDirectory));
        Assert.Empty(Directory.GetFiles(fixture.LongTermDirectory));

        await fixture.RunSinglePollAsync();

        Assert.False(File.Exists(inputPath));
        Assert.True(File.Exists(Path.Combine(fixture.QuarantineDirectory, "bad-output.MTS")));
        Assert.Empty(Directory.GetFiles(fixture.BackupDirectory));
        Assert.Empty(Directory.GetFiles(fixture.LongTermDirectory));
    }

    [Fact]
    public async Task DatabaseInsertFailure_CleansCopies_RetainsInputForRetry_ThenQuarantines()
    {
        await using var fixture = await IngestionFixture.CreateAsync();
        var inputPath = await fixture.CreateMediaInputAsync("db-failure.MTS");

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER RejectVideoInsert
                BEFORE INSERT ON Videos
                BEGIN
                    SELECT RAISE(ABORT, 'integration test insert failure');
                END;
                """
            );
        }

        await fixture.RunSinglePollAsync();

        Assert.True(File.Exists(inputPath));
        Assert.False(File.Exists(Path.ChangeExtension(inputPath, ".mp4")));
        Assert.Empty(Directory.GetFiles(fixture.BackupDirectory));
        Assert.Empty(Directory.GetFiles(fixture.LongTermDirectory));

        await fixture.RunSinglePollAsync();

        Assert.False(File.Exists(inputPath));
        Assert.True(File.Exists(Path.Combine(fixture.QuarantineDirectory, "db-failure.MTS")));
        Assert.Empty(Directory.GetFiles(fixture.BackupDirectory));
        Assert.Empty(Directory.GetFiles(fixture.LongTermDirectory));

        await using var verificationScope = fixture.Services.CreateAsyncScope();
        var verificationContext =
            verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await verificationContext.Videos.ToListAsync());
    }

    private sealed class IngestionFixture : IAsyncDisposable
    {
        private static readonly MethodInfo ProcessFolderMethod =
            typeof(VideoIngestionBackgroundService).GetMethod(
                "ProcessFolder",
                BindingFlags.Instance | BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException("Unable to find the service poll method.");

        private readonly string _rootDirectory;
        private readonly string? _originalPath;
        private readonly string? _originalOmitCaptureDate;
        private readonly VideoIngestionBackgroundService _service;

        private IngestionFixture(
            string rootDirectory,
            string? originalPath,
            string? originalOmitCaptureDate,
            ServiceProvider services,
            VideoIngestionBackgroundService service
        )
        {
            _rootDirectory = rootDirectory;
            _originalPath = originalPath;
            _originalOmitCaptureDate = originalOmitCaptureDate;
            Services = services;
            _service = service;
            InputDirectory = Path.Combine(rootDirectory, "input");
            BackupDirectory = Path.Combine(rootDirectory, "backup");
            LongTermDirectory = Path.Combine(rootDirectory, "long-term");
            QuarantineDirectory = Path.Combine(rootDirectory, "quarantine");
        }

        public ServiceProvider Services { get; }
        public string InputDirectory { get; }
        public string BackupDirectory { get; }
        public string LongTermDirectory { get; }
        public string QuarantineDirectory { get; }

        public static async Task<IngestionFixture> CreateAsync()
        {
            var rootDirectory = Path.Combine(
                Path.GetTempPath(),
                "HomeVideoPlayer.IntegrationTests",
                Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(rootDirectory);

            var inputDirectory = Path.Combine(rootDirectory, "input");
            var backupDirectory = Path.Combine(rootDirectory, "backup");
            var longTermDirectory = Path.Combine(rootDirectory, "long-term");
            var quarantineDirectory = Path.Combine(rootDirectory, "quarantine");
            var toolDirectory = Path.Combine(rootDirectory, "tools");

            Directory.CreateDirectory(inputDirectory);
            Directory.CreateDirectory(backupDirectory);
            Directory.CreateDirectory(longTermDirectory);
            Directory.CreateDirectory(quarantineDirectory);
            Directory.CreateDirectory(toolDirectory);

            var originalPath = Environment.GetEnvironmentVariable("PATH");
            var originalOmitCaptureDate = Environment.GetEnvironmentVariable(
                "HVP_TEST_OMIT_CAPTURE_DATE"
            );
            var realExifToolPath = FindExecutablePath("exiftool", originalPath);
            var exifToolWrapperPath = Path.Combine(toolDirectory, "ExifTool");
            await File.WriteAllTextAsync(
                exifToolWrapperPath,
                $"#!/bin/sh\nset -e\n'{realExifToolPath}' \"$@\" | jq 'map(if env.HVP_TEST_OMIT_CAPTURE_DATE == \"1\" then . else . + {{\"DateTimeOriginal\": \"2026:05:18 19:52:03+01:00\"}} end)'\n"
            );
            if (OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException(
                    "The integration test ExifTool wrapper requires a Unix-like operating system."
                );
            }

            File.SetUnixFileMode(
                exifToolWrapperPath,
                UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead
                    | UnixFileMode.OtherExecute
            );
            Environment.SetEnvironmentVariable(
                "PATH",
                string.Join(Path.PathSeparator, toolDirectory, originalPath)
            );

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite(
                    $"Data Source={Path.Combine(rootDirectory, "test.db")};Pooling=False"
                )
            );

            var serviceProvider = services.BuildServiceProvider();
            await using (var scope = serviceProvider.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await dbContext.Database.MigrateAsync();
            }

            var options = Options.Create(
                new VideoIngestionOptions
                {
                    InputDirectory = inputDirectory,
                    BackupStorage = backupDirectory,
                    LongTermStorage = longTermDirectory,
                    QuarantineDirectory = quarantineDirectory,
                    SupportedFileTypes = [".MTS"],
                    PollIntervalSeconds = 3600,
                }
            );

            var service = new VideoIngestionBackgroundService(
                serviceProvider.GetRequiredService<ILogger<VideoIngestionBackgroundService>>(),
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                options
            );

            return new IngestionFixture(
                rootDirectory,
                originalPath,
                originalOmitCaptureDate,
                serviceProvider,
                service
            );
        }

        public async Task<string> CreateMediaInputAsync(string fileName)
        {
            var filePath = Path.Combine(InputDirectory, fileName);
            await RunProcessAsync(
                "ffmpeg",
                [
                    "-hide_banner",
                    "-loglevel",
                    "error",
                    "-y",
                    "-f",
                    "lavfi",
                    "-i",
                    "color=c=black:s=64x64:r=25:d=1",
                    "-f",
                    "lavfi",
                    "-i",
                    "sine=frequency=1000:sample_rate=48000:duration=1",
                    "-c:v",
                    "libx264",
                    "-pix_fmt",
                    "yuv420p",
                    "-c:a",
                    "ac3",
                    "-f",
                    "mpegts",
                    filePath,
                ]
            );

            return filePath;
        }

        public async Task RunSinglePollAsync()
        {
            var task = (Task?)ProcessFolderMethod.Invoke(_service, [CancellationToken.None]);
            Assert.NotNull(task);
            await task;
        }

        public void OmitCaptureDate() =>
            Environment.SetEnvironmentVariable("HVP_TEST_OMIT_CAPTURE_DATE", "1");

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            Environment.SetEnvironmentVariable("PATH", _originalPath);
            Environment.SetEnvironmentVariable(
                "HVP_TEST_OMIT_CAPTURE_DATE",
                _originalOmitCaptureDate
            );
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, recursive: true);
            }
        }

        private static async Task RunProcessAsync(string executable, IEnumerable<string> arguments)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"{executable} exited with code {process.ExitCode}. stdout: {stdout} stderr: {stderr}"
                );
            }
        }

        private static string FindExecutablePath(string executableName, string? path)
        {
            foreach (var directory in (path ?? string.Empty).Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                var executablePath = Path.Combine(directory, executableName);
                if (File.Exists(executablePath))
                {
                    return executablePath;
                }
            }

            throw new FileNotFoundException(
                $"Unable to find required executable '{executableName}' on PATH."
            );
        }
    }
}
