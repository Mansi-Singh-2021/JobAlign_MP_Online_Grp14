using JobAlign.Core.Entities.Skills;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobAlign.Infrastructure.Data.Configurations;

/// <summary>
/// Master skills (FR-57). The unique index on NormalizedName is what stops the
/// list fragmenting into "C#", "c#" and "C #" (risk R-03).
/// </summary>
public class MasterSkillConfiguration : IEntityTypeConfiguration<MasterSkill>
{
    public void Configure(EntityTypeBuilder<MasterSkill> builder)
    {
        builder.ToTable("MasterSkills");

        builder.Property(x => x.Name).HasMaxLength(128).IsRequired();
        builder.Property(x => x.NormalizedName).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(64);

        builder.HasIndex(x => x.NormalizedName)
            .IsUnique()
            .HasDatabaseName("UX_MasterSkills_NormalizedName");

        // A merged skill points at its survivor and is kept, never deleted (FR-58),
        // so existing postings and profiles can still be resolved.
        builder.HasOne(x => x.MergedIntoMasterSkill)
            .WithMany()
            .HasForeignKey(x => x.MergedIntoMasterSkillId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

/// <summary>
/// Skill aliases (FR-14, FR-58). NormalizedAlias is unique across the whole
/// table: one spelling cannot resolve to two different master skills, which is
/// what makes BR-04 decidable.
/// </summary>
public class SkillAliasConfiguration : IEntityTypeConfiguration<SkillAlias>
{
    public void Configure(EntityTypeBuilder<SkillAlias> builder)
    {
        builder.ToTable("SkillAliases");

        builder.Property(x => x.Alias).HasMaxLength(128).IsRequired();
        builder.Property(x => x.NormalizedAlias).HasMaxLength(128).IsRequired();

        builder.HasIndex(x => x.NormalizedAlias)
            .IsUnique()
            .HasDatabaseName("UX_SkillAliases_NormalizedAlias");

        builder.HasOne(x => x.MasterSkill)
            .WithMany(x => x.Aliases)
            .HasForeignKey(x => x.MasterSkillId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Normalized locations (FR-16).</summary>
public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("Locations");

        builder.Property(x => x.CanonicalName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NormalizedName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.City).HasMaxLength(100);
        builder.Property(x => x.Region).HasMaxLength(100);
        builder.Property(x => x.Country).HasMaxLength(100);

        builder.HasIndex(x => x.NormalizedName)
            .IsUnique()
            .HasDatabaseName("UX_Locations_NormalizedName");
    }
}

/// <summary>Location aliases, e.g. "Bangalore" resolving to "Bengaluru" (FR-16).</summary>
public class LocationAliasConfiguration : IEntityTypeConfiguration<LocationAlias>
{
    public void Configure(EntityTypeBuilder<LocationAlias> builder)
    {
        builder.ToTable("LocationAliases");

        builder.Property(x => x.Alias).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NormalizedAlias).HasMaxLength(200).IsRequired();

        builder.HasIndex(x => x.NormalizedAlias)
            .IsUnique()
            .HasDatabaseName("UX_LocationAliases_NormalizedAlias");

        builder.HasOne(x => x.Location)
            .WithMany(x => x.Aliases)
            .HasForeignKey(x => x.LocationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
