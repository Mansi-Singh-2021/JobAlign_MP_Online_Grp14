using JobAlign.Core.Entities.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobAlign.Infrastructure.Data.Configurations;

/// <summary>
/// Administrative audit log (FR-60). Append-only by convention in the
/// application layer; the actor FK is SetNull so an entry outlives the account
/// that made the change.
/// </summary>
public class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditEntries");

        builder.Property(x => x.Action).HasMaxLength(128).IsRequired();
        builder.Property(x => x.EntityName).HasMaxLength(128).IsRequired();
        builder.Property(x => x.EntityId).HasMaxLength(64);

        builder.HasIndex(x => x.OccurredAt)
            .HasDatabaseName("IX_AuditEntries_OccurredAt");

        builder.HasIndex(x => new { x.EntityName, x.EntityId })
            .HasDatabaseName("IX_AuditEntries_Entity");

        builder.HasOne(x => x.ActorUser)
            .WithMany()
            .HasForeignKey(x => x.ActorUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
