using JobAlign.Core.Entities.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobAlign.Infrastructure.Data.Configurations;

/// <summary>Candidate profile (FR-27, FR-33). One per user; owner-only by BR-09.</summary>
public class CandidateProfileConfiguration : IEntityTypeConfiguration<CandidateProfile>
{
    public void Configure(EntityTypeBuilder<CandidateProfile> builder)
    {
        builder.ToTable("CandidateProfiles");

        builder.Property(x => x.FullName).HasMaxLength(200);
        builder.Property(x => x.Headline).HasMaxLength(256);
        builder.Property(x => x.CurrentRole).HasMaxLength(128);
        builder.Property(x => x.PhoneNumber).HasMaxLength(32);

        // Nullable: no recorded experience is not the same as zero years (BR-02).
        builder.Property(x => x.TotalExperienceYears).HasPrecision(5, 2);

        builder.HasIndex(x => x.UserId)
            .IsUnique()
            .HasDatabaseName("UX_CandidateProfiles_UserId");

        builder.HasOne(x => x.Location)
            .WithMany()
            .HasForeignKey(x => x.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class EducationEntryConfiguration : IEntityTypeConfiguration<EducationEntry>
{
    public void Configure(EntityTypeBuilder<EducationEntry> builder)
    {
        builder.ToTable("EducationEntries");

        builder.Property(x => x.Institution).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Qualification).HasMaxLength(200);
        builder.Property(x => x.FieldOfStudy).HasMaxLength(200);
        builder.Property(x => x.Grade).HasMaxLength(50);

        builder.HasOne(x => x.CandidateProfile)
            .WithMany(x => x.Education)
            .HasForeignKey(x => x.CandidateProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Work history (FR-27); the source of the total in FR-33.</summary>
public class WorkExperienceEntryConfiguration : IEntityTypeConfiguration<WorkExperienceEntry>
{
    public void Configure(EntityTypeBuilder<WorkExperienceEntry> builder)
    {
        builder.ToTable("WorkExperienceEntries");

        builder.Property(x => x.CompanyName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.JobTitle).HasMaxLength(200).IsRequired();

        builder.HasOne(x => x.CandidateProfile)
            .WithMany(x => x.WorkExperience)
            .HasForeignKey(x => x.CandidateProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProjectEntryConfiguration : IEntityTypeConfiguration<ProjectEntry>
{
    public void Configure(EntityTypeBuilder<ProjectEntry> builder)
    {
        builder.ToTable("ProjectEntries");

        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Url).HasMaxLength(2048);

        builder.HasOne(x => x.CandidateProfile)
            .WithMany(x => x.Projects)
            .HasForeignKey(x => x.CandidateProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class CertificationEntryConfiguration : IEntityTypeConfiguration<CertificationEntry>
{
    public void Configure(EntityTypeBuilder<CertificationEntry> builder)
    {
        builder.ToTable("CertificationEntries");

        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.IssuingOrganization).HasMaxLength(200);
        builder.Property(x => x.CredentialId).HasMaxLength(128);

        builder.HasOne(x => x.CandidateProfile)
            .WithMany(x => x.Certifications)
            .HasForeignKey(x => x.CandidateProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Confirmed profile skills (FR-28, FR-29). Only rows in this table feed match
/// scoring — resume suggestions live elsewhere until accepted (BR-06).
/// </summary>
public class ProfileSkillConfiguration : IEntityTypeConfiguration<ProfileSkill>
{
    public void Configure(EntityTypeBuilder<ProfileSkill> builder)
    {
        builder.ToTable("ProfileSkills");

        builder.Property(x => x.ProficiencyLevel).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Source).HasConversion<string>().HasMaxLength(24).IsRequired();

        builder.HasIndex(x => new { x.CandidateProfileId, x.MasterSkillId })
            .IsUnique()
            .HasDatabaseName("UX_ProfileSkills_Profile_Skill");

        builder.HasOne(x => x.CandidateProfile)
            .WithMany(x => x.Skills)
            .HasForeignKey(x => x.CandidateProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.MasterSkill)
            .WithMany(x => x.ProfileSkills)
            .HasForeignKey(x => x.MasterSkillId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Uploaded resumes (FR-30). Candidate-deletable per FR-34 and NFR-09.</summary>
public class ResumeConfiguration : IEntityTypeConfiguration<Resume>
{
    public void Configure(EntityTypeBuilder<Resume> builder)
    {
        builder.ToTable("Resumes");

        builder.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ExtractionStatus).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.FailureReason).HasMaxLength(1024);

        builder.HasOne(x => x.CandidateProfile)
            .WithMany(x => x.Resumes)
            .HasForeignKey(x => x.CandidateProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Resume-extracted skills awaiting confirmation (FR-31, FR-32).
/// Separate from ProfileSkills so that BR-06 holds structurally: nothing here
/// can reach the match calculation.
/// </summary>
public class ResumeSkillSuggestionConfiguration : IEntityTypeConfiguration<ResumeSkillSuggestion>
{
    public void Configure(EntityTypeBuilder<ResumeSkillSuggestion> builder)
    {
        builder.ToTable("ResumeSkillSuggestions");

        builder.Property(x => x.RawText).HasMaxLength(256).IsRequired();

        builder.HasIndex(x => new { x.ResumeId, x.RawText })
            .IsUnique()
            .HasDatabaseName("UX_ResumeSkillSuggestions_Resume_RawText");

        builder.HasOne(x => x.Resume)
            .WithMany(x => x.SkillSuggestions)
            .HasForeignKey(x => x.ResumeId)
            .OnDelete(DeleteBehavior.Cascade);

        // Nullable: an unrecognised name is still shown to the candidate, but it
        // must resolve to a master skill before it can be confirmed (BR-04).
        builder.HasOne(x => x.MasterSkill)
            .WithMany()
            .HasForeignKey(x => x.MasterSkillId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
