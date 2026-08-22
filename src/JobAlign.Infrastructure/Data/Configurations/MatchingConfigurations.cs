using JobAlign.Core.Entities.Matching;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobAlign.Infrastructure.Data.Configurations;

/// <summary>
/// Match results (FR-36 to FR-40). All four scores are stored so the outcome can
/// be explained, and all are nullable so an unmeasurable component is recorded
/// as unmeasurable rather than as zero (BR-02).
/// </summary>
public class MatchResultConfiguration : IEntityTypeConfiguration<MatchResult>
{
    public void Configure(EntityTypeBuilder<MatchResult> builder)
    {
        builder.ToTable("MatchResults");

        builder.Property(x => x.RequiredSkillScore).HasPrecision(5, 2);
        builder.Property(x => x.PreferredSkillScore).HasPrecision(5, 2);
        builder.Property(x => x.ExperienceScore).HasPrecision(5, 2);
        builder.Property(x => x.OverallScore).HasPrecision(5, 2);

        builder.Property(x => x.ScoringConfigVersion).HasMaxLength(64).IsRequired();

        builder.HasIndex(x => x.JobPostingId)
            .IsUnique()
            .HasDatabaseName("UX_MatchResults_JobPostingId");

        // Supports sorting the dashboard by match score (FR-51, NFR-02).
        builder.HasIndex(x => new { x.CandidateProfileId, x.OverallScore })
            .HasDatabaseName("IX_MatchResults_Profile_OverallScore");

        builder.HasOne(x => x.JobPosting)
            .WithOne(x => x.MatchResult)
            .HasForeignKey<MatchResult>(x => x.JobPostingId)
            .OnDelete(DeleteBehavior.Cascade);

        // NoAction: the posting path above already cascades from the user, and a
        // second cascade path through CandidateProfile would be a multiple
        // cascade path. Deleting a profile is handled in the application layer.
        builder.HasOne(x => x.CandidateProfile)
            .WithMany()
            .HasForeignKey(x => x.CandidateProfileId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

/// <summary>Missing skills per posting (FR-42, FR-43).</summary>
public class SkillGapConfiguration : IEntityTypeConfiguration<SkillGap>
{
    public void Configure(EntityTypeBuilder<SkillGap> builder)
    {
        builder.ToTable("SkillGaps");

        builder.Property(x => x.SkillType).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.HasIndex(x => new { x.MatchResultId, x.MasterSkillId })
            .IsUnique()
            .HasDatabaseName("UX_SkillGaps_MatchResult_Skill");

        builder.HasOne(x => x.MatchResult)
            .WithMany(x => x.SkillGaps)
            .HasForeignKey(x => x.MatchResultId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.MasterSkill)
            .WithMany(x => x.SkillGaps)
            .HasForeignKey(x => x.MasterSkillId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Learning roadmap (FR-46, FR-47).</summary>
public class RoadmapItemConfiguration : IEntityTypeConfiguration<RoadmapItem>
{
    public void Configure(EntityTypeBuilder<RoadmapItem> builder)
    {
        builder.ToTable("RoadmapItems");

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.HasIndex(x => new { x.CandidateProfileId, x.MasterSkillId })
            .IsUnique()
            .HasDatabaseName("UX_RoadmapItems_Profile_Skill");

        builder.HasIndex(x => new { x.CandidateProfileId, x.Priority })
            .HasDatabaseName("IX_RoadmapItems_Profile_Priority");

        builder.HasOne(x => x.CandidateProfile)
            .WithMany(x => x.RoadmapItems)
            .HasForeignKey(x => x.CandidateProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.MasterSkill)
            .WithMany(x => x.RoadmapItems)
            .HasForeignKey(x => x.MasterSkillId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
