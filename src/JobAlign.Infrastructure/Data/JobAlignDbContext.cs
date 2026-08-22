using JobAlign.Core.Entities.Admin;
using JobAlign.Core.Entities.Identity;
using JobAlign.Core.Entities.Matching;
using JobAlign.Core.Entities.Postings;
using JobAlign.Core.Entities.Profiles;
using JobAlign.Core.Entities.Skills;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace JobAlign.Infrastructure.Data;

/// <summary>
/// The JobAlign database context. Extends Identity so users, roles and their
/// claims share one store and one transaction with the domain tables
/// (FR-01 to FR-03, NFR-05).
/// </summary>
public class JobAlignDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, int>
{
    public JobAlignDbContext(DbContextOptions<JobAlignDbContext> options)
        : base(options)
    {
    }

    // Skills and locations — master data (FR-14, FR-16, FR-57, FR-58)
    public DbSet<MasterSkill> MasterSkills => Set<MasterSkill>();
    public DbSet<SkillAlias> SkillAliases => Set<SkillAlias>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<LocationAlias> LocationAliases => Set<LocationAlias>();

    // Postings (FR-06 to FR-26)
    public DbSet<JobPosting> JobPostings => Set<JobPosting>();
    public DbSet<PostingExtraction> PostingExtractions => Set<PostingExtraction>();
    public DbSet<ExtractionFieldConfidence> ExtractionFieldConfidences => Set<ExtractionFieldConfidence>();
    public DbSet<PostingFieldCorrection> PostingFieldCorrections => Set<PostingFieldCorrection>();
    public DbSet<PostingSkill> PostingSkills => Set<PostingSkill>();
    public DbSet<PostingRelation> PostingRelations => Set<PostingRelation>();
    public DbSet<PostingQualityAssessment> PostingQualityAssessments => Set<PostingQualityAssessment>();

    // Profile and resume (FR-27 to FR-34)
    public DbSet<CandidateProfile> CandidateProfiles => Set<CandidateProfile>();
    public DbSet<EducationEntry> EducationEntries => Set<EducationEntry>();
    public DbSet<WorkExperienceEntry> WorkExperienceEntries => Set<WorkExperienceEntry>();
    public DbSet<ProjectEntry> ProjectEntries => Set<ProjectEntry>();
    public DbSet<CertificationEntry> CertificationEntries => Set<CertificationEntry>();
    public DbSet<ProfileSkill> ProfileSkills => Set<ProfileSkill>();
    public DbSet<Resume> Resumes => Set<Resume>();
    public DbSet<ResumeSkillSuggestion> ResumeSkillSuggestions => Set<ResumeSkillSuggestion>();

    // Matching, gaps and roadmap (FR-35 to FR-47)
    public DbSet<MatchResult> MatchResults => Set<MatchResult>();
    public DbSet<SkillGap> SkillGaps => Set<SkillGap>();
    public DbSet<RoadmapItem> RoadmapItems => Set<RoadmapItem>();

    // Administration (FR-60)
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(JobAlignDbContext).Assembly);
    }
}
