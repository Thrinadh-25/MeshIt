using System.IO;
using meshIt.Models;
using Microsoft.EntityFrameworkCore;

namespace meshIt.Data;

/// <summary>
/// Entity Framework Core SQLite context for persisting chat messages.
/// Database stored at %APPDATA%\meshIt\messages.db.
/// </summary>
public class AppDbContext : DbContext
{
    private static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "meshIt", "messages.db");

    public DbSet<Message> Messages => Set<Message>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        options.UseSqlite($"Data Source={DbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.HasIndex(m => m.PeerId);
            entity.HasIndex(m => m.Timestamp);
        });
    }
}
