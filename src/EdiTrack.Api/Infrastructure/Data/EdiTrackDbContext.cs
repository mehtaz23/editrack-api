using EdiTrack.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EdiTrack.Api.Infrastructure.Data;

public class EdiTrackDbContext : DbContext
{
    public EdiTrackDbContext(DbContextOptions<EdiTrackDbContext> options) : base(options) { }

    public DbSet<EdiTransaction> Transactions => Set<EdiTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EdiTransaction>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Status)
                .HasConversion<string>()
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(t => t.SenderId).IsRequired().HasMaxLength(50);
            entity.Property(t => t.ReceiverId).IsRequired().HasMaxLength(50);
            entity.Property(t => t.TransactionType).IsRequired().HasMaxLength(50);
            entity.Property(t => t.CorrelationId).IsRequired().HasMaxLength(100);
            entity.Property(t => t.Payload).IsRequired();
            entity.Property(t => t.ReceivedAt).IsRequired();
        });
    }
}
