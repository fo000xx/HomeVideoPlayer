using System.Diagnostics;
using System.Globalization;
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

    private readonly HashSet<string> _quarantinedFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _retryCountByFile = new(
        StringComparer.OrdinalIgnoreCase
    );

    // todo-bf: add to program.cs on startup
    // todo-bf: add getting the options into program.cs, for this specific section -> also do the config validation
    // todo-bf: remove the .mp4 from InputFolder
    // todo-bf: why are 00000 - 00002 being copied to quarantine, and longterm storage? something is wrong here
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
        var files = Directory
            .GetFiles(_options.InputDirectory)
            .Where(path =>
                _options.SupportedFileTypes.Contains(
                    Path.GetExtension(path),
                    StringComparer.OrdinalIgnoreCase
                )
            )
            .Where(path => !_quarantinedFiles.Contains(path))
            .ToArray();

        if (files.Length == 0)
        {
            return;
        }

        var fileDateTimes = await GetMetadataFromFile(files, cancellationToken);

        foreach (var filePath in files)
        {
            if (_quarantinedFiles.Contains(filePath))
            {
                continue;
            }

            if (!fileDateTimes.TryGetValue(filePath, out var fileDate))
            {
                RegisterFailure(filePath);
                continue;
            }

            var attempts = _retryCountByFile.GetValueOrDefault(filePath);

            var succeeded = await ProcessSingleFileAsync(filePath, fileDate, cancellationToken);

            if (!succeeded)
            {
                RegisterFailure(filePath);
                continue;
            }

            _retryCountByFile.Remove(filePath);
        }
    }

    private void RegisterFailure(string filePath)
    {
        var attempts = _retryCountByFile.GetValueOrDefault(filePath);

        if (attempts >= 1)
        {
            QuarantineFile(filePath, "Failed after second attempt.");
            _quarantinedFiles.Add(filePath);
            _retryCountByFile.Remove(filePath);
            return;
        }

        _retryCountByFile[filePath] = attempts + 1;
        _logger.LogWarning(
            "File failed processing. Retrying once more: {FilePath}. Attempt {Attempt}/2",
            filePath,
            attempts + 1
        );
    }

    private async Task<bool> ProcessSingleFileAsync(
        string filePath,
        DateTime fileDate,
        CancellationToken cancellationToken
    )
    {
        string? backupFilePath = null;
        string? convertedFilePath = null;
        string? longTermFilePath = null;
        var metadataSaved = false;

        try
        {
            var originalFileHash = ComputeFileHash(filePath);

            var backupResult = await CopyFile(
                filePath,
                originalFileHash,
                _options.BackupStorage,
                cancellationToken
            );

            if (backupResult.IsError)
            {
                return false;
            }
            backupFilePath = backupResult.Value;

            var conversionResult = await ConvertFileContainer(filePath, cancellationToken);
            if (conversionResult.IsError)
            {
                return false;
            }

            var convertedFile = conversionResult.Value;
            convertedFilePath = convertedFile;
            var newFileHash = ComputeFileHash(convertedFile);
            var longTermCopyResult = await CopyFile(
                convertedFile,
                newFileHash,
                _options.LongTermStorage,
                cancellationToken
            );

            if (longTermCopyResult.IsError)
            {
                return false;
            }

            longTermFilePath = longTermCopyResult.Value;

            using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            dbContext.Videos.Add(
                new Video
                {
                    VideoName = Path.GetFileName(longTermFilePath),
                    StoragePath = Path.GetRelativePath(_options.LongTermStorage, longTermFilePath),
                    BackupPath = Path.GetRelativePath(_options.BackupStorage, backupFilePath),
                    VideoDate = fileDate,
                    OriginalFileHash = originalFileHash,
                    NewFileHash = newFileHash,
                }
            );

            await dbContext.SaveChangesAsync(cancellationToken);
            metadataSaved = true;

            File.Delete(filePath);
            File.Delete(convertedFilePath);
            return true;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to save metadata for {FilePath}", filePath);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected failure processing {FilePath}", filePath);
            return false;
        }
        finally
        {
            TryDeleteGeneratedFile(convertedFilePath);

            if (!metadataSaved)
            {
                TryDeleteGeneratedFile(backupFilePath);
                TryDeleteGeneratedFile(longTermFilePath);
            }
        }
    }

    private void TryDeleteGeneratedFile(string? generatedFilePath)
    {
        if (string.IsNullOrWhiteSpace(generatedFilePath))
        {
            return;
        }

        try
        {
            File.Delete(generatedFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Unable to clean up generated file {FilePath}",
                generatedFilePath
            );
        }
    }

    private void QuarantineFile(string filePath, string reason)
    {
        Directory.CreateDirectory(_options.QuarantineDirectory);

        var quarantinePath = Path.Combine(_options.QuarantineDirectory, Path.GetFileName(filePath));

        if (File.Exists(quarantinePath))
        {
            File.Delete(quarantinePath);
        }

        File.Move(filePath, quarantinePath);

        _logger.LogError("Quarantined file {FilePath} because: {Reason}", filePath, reason);
    }

    private async Task<ErrorOr<string>> CopyFile(
        string filePath,
        string originalHash,
        string copyLocation,
        CancellationToken cancellationToken
    )
    {
        var destinationPath = Path.Combine(copyLocation, Path.GetFileName(filePath));
        var destinationCreated = false;
        try
        {
            await using var sourceStream = File.OpenRead(filePath);
            await using var destStream = File.Create(destinationPath);
            destinationCreated = true;
            await sourceStream.CopyToAsync(destStream, cancellationToken);
        }
        catch (Exception ex)
        {
            if (destinationCreated)
            {
                TryDeleteGeneratedFile(destinationPath);
            }

            _logger.LogError(
                ex,
                "Unable to copy {FilePath} to {CopyLocation}",
                filePath,
                copyLocation
            );
            return Error.Failure(code: "FileCopyFailed", description: ex.Message);
        }

        try
        {
            if (originalHash != ComputeFileHash(destinationPath))
            {
                _logger.LogError(
                    "Hash mismatch copying {FilePath} to {DestinationPath}",
                    filePath,
                    destinationPath
                );
                TryDeleteGeneratedFile(destinationPath);
                return Error.Failure(
                    code: "FileCopyHashMismatch",
                    description: "Copied file hash does not match the source."
                );
            }
        }
        catch (Exception ex)
        {
            TryDeleteGeneratedFile(destinationPath);
            _logger.LogError(ex, "Unable to verify copied file {DestinationPath}", destinationPath);
            return Error.Failure(code: "FileCopyVerificationFailed", description: ex.Message);
        }

        return destinationPath;
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
            process.StartInfo.ArgumentList.Add("-nostdin");
            process.StartInfo.ArgumentList.Add("-y");
            process.StartInfo.ArgumentList.Add("-i");
            process.StartInfo.ArgumentList.Add(inFile);
            process.StartInfo.ArgumentList.Add("-c:v");
            process.StartInfo.ArgumentList.Add("copy");
            process.StartInfo.ArgumentList.Add("-c:a");
            process.StartInfo.ArgumentList.Add("aac");
            process.StartInfo.ArgumentList.Add("-b:a");
            process.StartInfo.ArgumentList.Add("192k");
            process.StartInfo.ArgumentList.Add(outFile);

            process.Start();

            var errorOutput = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                _logger.LogError("VideoConversionFailed: {}", errorOutput);
                TryDeleteGeneratedFile(outFile);
                return Error.Failure(code: "VideoConversionFailed", description: errorOutput);
            }

            return outFile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to convert {FilePath}.", inFile);
            TryDeleteGeneratedFile(outFile);

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
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    raw = System.Text.RegularExpressions.Regex.Replace(
                        raw,
                        @"\s+[A-Za-z]{2,5}$",
                        string.Empty
                    );
                }

                var formats = new[]
                {
                    "yyyy:MM:dd HH:mm:ss",
                    "yyyy:MM:dd HH:mm:sszzz",
                    "yyyy:MM:dd HH:mm:ss zzz",
                    "yyyy-MM-dd HH:mm:ss",
                    "yyyy-MM-ddTHH:mm:ss",
                    "yyyy-MM-ddTHH:mm:ssK",
                };

                if (
                    DateTime.TryParseExact(
                        raw,
                        formats,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var parsed
                    )
                )
                {
                    results[sourceFile] = parsed;
                }
                else
                {
                    _logger.LogError("Unable to fetch creation date for {SourceFile}.", sourceFile);
                }
            }
        }
        return results;
    }
}
