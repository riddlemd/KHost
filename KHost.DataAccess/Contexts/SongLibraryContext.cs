using KHost.Abstractions.Models;
using KHost.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace KHost.DataAccess.Contexts;

public class SongLibraryContext : DbContext
{
    public DbSet<ISong> Songs { get; set; }

    public SongLibraryContext(DbContextOptions<SongLibraryContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ISong>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.FilePath)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.Title)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.Artist)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.Album)
                .HasMaxLength(255);

            entity.Property(e => e.Format)
                .IsRequired()
                .HasMaxLength(10);

            entity.Property(e => e.Notes)
                .HasMaxLength(1000);

            // Indexes for common queries
            entity.HasIndex(e => e.FilePath).IsUnique();
            entity.HasIndex(e => e.Title);
            entity.HasIndex(e => e.Artist);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.DateAdded);
        });
    }
}
