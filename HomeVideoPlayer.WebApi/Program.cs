using HomeVideoPlayer.WebApi.Features.VideoIngestion;

var builder = Host.CreateApplicationBuilder(args);
var config = builder.Configuration.GetSection("VideoIngestionOptions");

builder
    .Services.AddOptions<VideoIngestionOptions>()
    .Bind(builder.Configuration.GetSection(VideoIngestionOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.InputDirectory),
        "VideoIngestionOptions:InputDirectory must be configured"
    )
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.LongTermStorage),
        "VideoIngestionOptions:LongTermStorage must be configured"
    )
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.BackupStorage),
        "VideoIngestionOptions:BackupStorage must be configured"
    )
    .ValidateOnStart();
;

builder.Services.AddHostedService<VideoIngestionBackgroundService>();

var host = builder.Build();
host.Run();
