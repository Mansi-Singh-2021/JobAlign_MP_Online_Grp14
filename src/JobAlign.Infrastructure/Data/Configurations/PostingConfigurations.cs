using JobAlign.Core.Entities.Postings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobAlign.Infrastructure.Data.Configurations;

/// <summary>Job postings (FR-06 to FR-11).</summary>
public class JobPostingConfiguration : IEntityTypeConfiguration<JobPosting>
{
    public void Configure(EntityTypeBuilder<JobPosting> builder)
    {
        builder.ToTable("JobPostings");

        // BR-01: the original text, unbounded and never altered. There is no
        // public setter on the entity; nothing in the data layer reintroduces one.
        builder.Property(x => x.RawText).IsRequired();

        builder.Property(x => x.Reference).HasMaxLength(32).IsRequired();
        builder.Property(x => x.SourceUrl).HasMaxLength(2048);
        builder.Property(x => x.SourceName).HasMaxLength(128);

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.ApplicationStatus).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.CaptureMethod).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.HasIndex(x => x.Reference)
            .IsUnique()
            .HasDatabaseName("UX_JobPostings_Reference");   // FR-09

        // Every posting query filters by owner first (BR-09, NFR-04); this index
        // is what keeps that filter cheap at the 500-posting target in NFR-02.
        builder.HasIndex(x => new { x.OwnerUserId, x.Status })
            .HasDatabaseName("IX_JobPostings_OwnerUserId_Status");

        builder.HasIndex(x => new { x.OwnerUserId, x.CapturedAt })
            .HasDatabaseName("IX_JobPostings_OwnerUserId_CapturedAt");   // FR-51 sort by date

        builder.HasOne(x => x.Owner)
            .WithMany(x => x.JobPostings)
            .HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.QualityAssessment)
            .WithOne(x => x.JobPosting)
            .HasForeignKey<PostingQualityAssessment>(x => x.JobPostingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Extraction runs (FR-12, FR-21). Every extracted column is nullable — that is
/// deliberate and required by BR-02/FR-17. Do not add IsRequired() to any of them.
/// </summary>
public class PostingExtractionConfiguration : IEntityTypeConfiguration<PostingExtraction>
{
    public void Configure(EntityTypeBuilder<PostingExtraction> builder)
    {
        builder.ToTable("PostingExtractions");

        builder.Property(x => x.ExtractionConfigVersion).HasMaxLength(64).IsRequired();
        builder.Property(x => x.FailureReason).HasMaxLength(1024);

        builder.Property(x => x.JobTitle).HasMaxLength(256);
        builder.Property(x => x.CompanyName).HasMaxLength(256);
        builder.Property(x => x.RawLocationText).HasMaxLength(256);
        builder.Property(x => x.SalaryCurrencyRaw).HasMaxLength(16);
        builder.Property(x => x.SalaryCurrencyNormalized).HasMaxLength(8);

        builder.Property(x => x.RunStatus).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.RemotePolicy).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.SalaryPeriodRaw).HasConversion<string>().HasMaxLength(16);

        builder.Property(x => x.ExperienceMinYears).HasPrecision(5, 2);
        builder.Property(x => x.ExperienceMaxYears).HasPrecision(5, 2);
        builder.Property(x => x.SalaryMinRaw).HasPrecision(18, 2);
        builder.Property(x => x.SalaryMaxRaw).HasPrecision(18, 2);
        builder.Property(x => x.SalaryMinYearly).HasPrecision(18, 2);
        builder.Property(x => x.SalaryMaxYearly).HasPrecision(18, 2);

        // At most one current run per posting, enforced by the database rather
        // than by application discipline.
        builder.HasIndex(x => x.JobPostingId)
            .IsUnique()
            .HasFilter("[IsCurrent] = 1")
            .HasDatabaseName("UX_PostingExtractions_CurrentPerPosting");

        builder.HasOne(x => x.JobPosting)
            .WithMany(x => x.Extractions)
            .HasForeignKey(x => x.JobPostingId)
            .OnDelete(DeleteBehavior.Cascade);

        // Master data is deactivated, never deleted, so a restrict here is safe
        // and stops a location disappearing out from under an extraction.
        builder.HasOne(x => x.Location)
            .WithMany()
            .HasForeignKey(x => x.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Per-field confidence indicators (FR-20, NFR-06).</summary>
public class ExtractionFieldConfidenceConfiguration : IEntityTypeConfiguration<ExtractionFieldConfidence>
{
    public void Configure(EntityTypeBuilder<ExtractionFieldConfidence> builder)
    {
        builder.ToTable("ExtractionFieldConfidences");

        builder.Property(x => x.FieldName).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Confidence).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Score).HasPrecision(5, 4);

        builder.HasIndex(x => new { x.PostingExtractionId, x.FieldName })
            .IsUnique()
            .HasDatabaseName("UX_ExtractionFieldConfidences_Extraction_Field");

        builder.HasOne(x => x.PostingExtraction)
            .WithMany(x => x.FieldConfidences)
            .HasForeignKey(x => x.PostingExtractionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Candidate corrections (FR-18, BR-03). Note the foreign key is to the posting,
/// not to an extraction: deleting and regenerating extractions leaves these rows
/// untouched, which is exactly the behaviour BR-03 demands.
/// </summary>
public class PostingFieldCorrectionConfiguration : IEntityTypeConfiguration<PostingFieldCorrection>
{
    public void Configure(EntityTypeBuilder<PostingFieldCorrection> builder)
    {
        builder.ToTable("PostingFieldCorrections");

        builder.Property(x => x.FieldName).HasMaxLength(64).IsRequired();

        // No max length: a corrected Responsibilities value can be long.
        // Nullable, and null means "the posting does not state this" (BR-02).
        builder.Property(x => x.CorrectedValue);

        builder.HasIndex(x => new { x.JobPostingId, x.FieldName })
            .IsUnique()
            .HasDatabaseName("UX_PostingFieldCorrections_Posting_Field");

        builder.HasOne(x => x.JobPosting)
            .WithMany(x => x.Corrections)
            .HasForeignKey(x => x.JobPostingId)
            .OnDelete(DeleteBehavior.Cascade);

        // NoAction, not Cascade: the posting already cascades from the user, and
        // a second cascade path to AspNetUsers is rejected by SQL Server.
        builder.HasOne(x => x.CorrectedBy)
            .WithMany()
            .HasForeignKey(x => x.CorrectedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

/// <summary>
/// Skills a posting asks for (FR-13). MasterSkillId is a foreign key, so a
/// free-text skill cannot be stored as an identity (BR-04).
/// </summary>
public class PostingSkillConfiguration : IEntityTypeConfiguration<PostingSkill>
{
    public void Configure(EntityTypeBuilder<PostingSkill> builder)
    {
        builder.ToTable("PostingSkills");

        builder.Property(x => x.RawText).HasMaxLength(256);
        builder.Property(x => x.SkillType).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Source).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.HasIndex(x => new { x.JobPostingId, x.MasterSkillId })
            .IsUnique()
            .HasDatabaseName("UX_PostingSkills_Posting_Skill");

        builder.HasOne(x => x.JobPosting)
            .WithMany(x => x.Skills)
            .HasForeignKey(x => x.JobPostingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.MasterSkill)
            .WithMany(x => x.PostingSkills)
            .HasForeignKey(x => x.MasterSkillId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Duplicate and same-role links (FR-24 to FR-26).</summary>
public class PostingRelationConfiguration : IEntityTypeConfiguration<PostingRelation>
{
    public void Configure(EntityTypeBuilder<PostingRelation> builder)
    {
        builder.ToTable("PostingRelations");

        builder.Property(x => x.RelationType).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.Resolution).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.SimilarityScore).HasPrecision(5, 4);

        builder.HasIndex(x => new { x.JobPostingId, x.RelatedJobPostingId })
            .IsUnique()
            .HasDatabaseName("UX_PostingRelations_Posting_Related");

        builder.HasOne(x => x.JobPosting)
            .WithMany(x => x.Relations)
            .HasForeignKey(x => x.JobPostingId)
            .OnDelete(DeleteBehavior.Cascade);

        // Both ends point at JobPostings; only one may cascade or SQL Server
        // refuses the constraint as a multiple cascade path.
        builder.HasOne(x => x.RelatedJobPosting)
            .WithMany()
            .HasForeignKey(x => x.RelatedJobPostingId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

/// <summary>Posting completeness (FR-22, FR-23).</summary>
public class PostingQualityAssessmentConfiguration : IEntityTypeConfiguration<PostingQualityAssessment>
{
    public void Configure(EntityTypeBuilder<PostingQualityAssessment> builder)
    {
        builder.ToTable("PostingQualityAssessments");

        builder.Property(x => x.CompletenessScore).HasPrecision(5, 2);
        builder.Property(x => x.MissingFields).IsRequired();

        builder.HasIndex(x => x.JobPostingId)
            .IsUnique()
            .HasDatabaseName("UX_PostingQualityAssessments_Posting");
    }
}
