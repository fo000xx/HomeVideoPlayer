using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ErrorOr;
using HomeVideoPlayer.WebApi.Data;
using HomeVideoPlayer.WebApi.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeVideoPlayer.WebApi.Features.VideoIngestion;

public class VideoIngestionBackgroundService : BackgroundService
{
    private readonly ILogger<VideoIngestionBackgroundService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly VideoIngestionOptions _options;

    // todo-bf: add to program.cs on startup
    // todo-bf: add getting the options into program.cs, for this specific section -> also do the config validation
    public VideoIngestionBackgroundService(
        ILogger<VideoIngestionBackgroundService> logger,
        IServiceScopeFactory scopeFactory,
        IOptions<VideoIngestionOptions> options
    )
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ProcessFolder(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error whilst processing folder.");
            }
            await Task.Delay(_options.PollIntervalSeconds, cancellationToken);
        }
    }

    private async Task ProcessFolder(CancellationToken cancellationToken)
    {
        var files = Directory.GetFiles(_options.InputDirectory);
        if (files.Length == 0)
        {
            return;
        }

        try
        {
            var fileDateTimes = await GetMetadataFromFile(files, cancellationToken);

            if (fileDateTimes.Count == 0)
            {
                _logger.LogInformation("No metadata to save.");
                return;
            }

            await ProcessFiles(fileDateTimes, cancellationToken);
        }
        catch (Exception) { }
    }

    private async Task ProcessFiles(
        Dictionary<string, DateTime> files,
        CancellationToken cancellationToken
    )
    {
        var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var file in files)
        {
            var fileHash = ComputeFileHash(file.Key);
            var backUpStorage = _options.BackupStorage;

            var copySucceeded = await CopyFile(
                file.Key,
                fileHash,
                backUpStorage,
                cancellationToken
            );
            if (!copySucceeded)
            {
                continue;
            }

            var conversionResult = await ConvertFileContainer(file.Key, cancellationToken);
            if (conversionResult.IsError)
            {
                continue;
            }

            var longTermStorage = _options.LongTermStorage;
            var convertedFile = conversionResult.Value;
            await CopyFile(convertedFile, fileHash, longTermStorage, cancellationToken);

            var video = new Video
            {
                VideoName = convertedFile,
                VideoDate = file.Value,
                FileHash = fileHash,
            };
            dbContext.Videos.Add(video);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);

                File.Delete(file.Key);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to save video metadata for {File}",
                    conversionResult.Value
                );

                File.Delete(convertedFile);
                File.Delete(backUpStorage);

                continue;
            }
        }
    }

    private async Task<bool> CopyFile(
        string filePath,
        string originalHash,
        string copyLocation,
        CancellationToken cancellationToken
    )
    {
        var destinationPath = Path.Combine(copyLocation, Path.GetFileName(filePath));
        try
        {
            await using var sourceStream = File.OpenRead(filePath);
            await using var destStream = File.Create(destinationPath);
            await sourceStream.CopyToAsync(destStream, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unable to copy {filePath} to long term storage: {longTermStorage}",
                filePath,
                copyLocation
            );
            return false;
        }

        if (originalHash != ComputeFileHash(copyLocation))
        {
            _logger.LogError("Hash of file before and after copy do not match. Ingestion Skipped.");
            File.Delete(copyLocation);
            return false;
        }
        return true;
    }

    private static string ComputeFileHash(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        byte[] hashBytes = sha256.ComputeHash(stream);
        return Convert.ToHexString(hashBytes);
    }

    private async Task<ErrorOr<string>> ConvertFileContainer(
        string inFile,
        CancellationToken cancellationToken
    )
    {
        var outFile = Path.ChangeExtension(inFile, ".mp4");

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            process.StartInfo.ArgumentList.Add("-i");
            process.StartInfo.ArgumentList.Add(inFile);
            process.StartInfo.ArgumentList.Add("-c:v copy -c:a aac -b:a 192k");
            process.StartInfo.ArgumentList.Add(outFile);

            process.Start();

            var errorOutput = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                return Error.Failure(code: "VideoConversionFailed", description: errorOutput);
            }

            return outFile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to convert {FilePath}.", inFile);

            return Error.Failure(code: "VideoConversionFailed", description: ex.Message);
        }
    }

    private async Task<Dictionary<string, DateTime>> GetMetadataFromFile(
        string[] files,
        CancellationToken cancellationToken
    )
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ExifTool",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        process.StartInfo.ArgumentList.Add("-DateTimeOriginal");
        // output as json
        process.StartInfo.ArgumentList.Add("-j");

        foreach (var file in files)
        {
            process.StartInfo.ArgumentList.Add(file);
        }

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            _logger.LogError(
                "exiftool exited with exit code {ExitCode}. stderr: {Error}",
                process.ExitCode,
                stderr
            );
            return [];
        }

        var results = new Dictionary<string, DateTime>();

        using var doc = JsonDocument.Parse(stdout);
        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            var sourceFile = entry.GetProperty("SourceFile").GetString()!;

            if (entry.TryGetProperty("DateTimeOriginal", out var dateProp))
            {
                var raw = dateProp.GetString();
                if (
                    DateTime.TryParseExact(
                        raw,
                        "yyyy:MM:dd HH:mm:ss",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None,
                        out var parsed
                    )
                )
                {
                    results[sourceFile] = parsed;
                }
                else
                {
                    _logger.LogError(
                        "Unable to fetch creation date for {SourceFile}. File will be skipped.",
                        sourceFile
                    );
                }
            }
        }
        return results;
    }
}
