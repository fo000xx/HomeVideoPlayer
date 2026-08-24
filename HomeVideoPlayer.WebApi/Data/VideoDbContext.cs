using HomeVideoPlayer.WebApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HomeVideoPlayer.WebApi.Data;

public class AppDbContext : DbContext
{
    public DbSet<Video> Videos { get; set; }
    public string DbPath { get; }

    public AppDbContext()
    {
        var folder = Environment.SpecialFolder.LocalApplicationData;
        var path = Environment.GetFolderPath(folder);
        DbPath = System.IO.Path.Join(path, "videos.db");
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseSqlite($"Data Source={DbPath}");
}
