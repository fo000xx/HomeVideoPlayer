using HomeVideoPlayer.WebApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HomeVideoPlayer.WebApi.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Video> Videos => Set<Video>();
    public DbSet<Tag> Tags => Set<Tag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tag>(entity =>
        {
            entity.Property(tag => tag.Name).UseCollation("NOCASE").HasMaxLength(100);
            entity.HasIndex(tag => tag.Name).IsUnique();
        });
    }
}
