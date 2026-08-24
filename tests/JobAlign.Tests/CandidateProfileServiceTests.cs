using JobAlign.Core.Abstractions;
using JobAlign.Core.Entities.Profiles;
using JobAlign.Core.Entities.Skills;
using JobAlign.Core.Enums;
using JobAlign.Infrastructure.Data;
using JobAlign.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobAlign.Tests;

public sealed class CandidateProfileServiceTests
{
    [Fact]
    public async Task AddSkill_resolves_through_the_master_list_and_stores_the_foreign_key()
    {
        await using var db = CreateDb();
        var profile = AddProfile(db, 10);
        db.MasterSkills.Add(new MasterSkill
        {
            Id = 7,
            Name = "C#",
            NormalizedName = "csharp",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var resolver = new StubResolver(new SkillResolution("c sharp", 7, "C#"));
        var service = CreateService(db, resolver);

        var result = await service.AddSkillAsync(10, "c sharp", ProficiencyLevel.Advanced);

        Assert.True(result.IsResolved);
        Assert.Equal(1, await db.ProfileSkills.CountAsync());
        var skill = await db.ProfileSkills.SingleAsync();
        Assert.Equal(7, skill.MasterSkillId);
        Assert.Equal(ProficiencyLevel.Advanced, skill.ProficiencyLevel);
        Assert.Equal(ProfileSkillSource.Manual, skill.Source);
        Assert.Equal(profile.Id, skill.CandidateProfileId);
    }

    [Fact]
    public async Task AddSkill_rejects_an_unresolved_skill_without_creating_a_master_skill()
    {
        await using var db = CreateDb();
        AddProfile(db, 10);
        await db.SaveChangesAsync();
        var service = CreateService(db, new StubResolver(new SkillResolution("Rust", null, null)));

        var result = await service.AddSkillAsync(10, "Rust", ProficiencyLevel.Beginner);

        Assert.False(result.IsResolved);
        Assert.Empty(db.ProfileSkills);
        Assert.Empty(db.MasterSkills);
    }

    [Fact]
    public async Task AddSkill_updates_proficiency_when_the_skill_is_already_held()
    {
        await using var db = CreateDb();
        var profile = AddProfile(db, 10);
        db.MasterSkills.Add(new MasterSkill { Id = 3, Name = "Python", NormalizedName = "python", IsActive = true });
        db.ProfileSkills.Add(new ProfileSkill
        {
            CandidateProfileId = profile.Id,
            MasterSkillId = 3,
            ProficiencyLevel = ProficiencyLevel.Beginner,
            Source = ProfileSkillSource.Manual,
            ConfirmedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, new StubResolver(new SkillResolution("Python", 3, "Python")));
        await service.AddSkillAsync(10, "Python", ProficiencyLevel.Expert);

        Assert.Equal(1, await db.ProfileSkills.CountAsync());
        Assert.Equal(ProficiencyLevel.Expert, (await db.ProfileSkills.SingleAsync()).ProficiencyLevel);
    }

    [Fact]
    public async Task RemoveSkill_only_removes_the_callers_own_skill()
    {
        await using var db = CreateDb();
        var own = AddProfile(db, 10);
        var other = AddProfile(db, 20);
        db.MasterSkills.Add(new MasterSkill { Id = 1, Name = "React", NormalizedName = "react", IsActive = true });
        db.ProfileSkills.AddRange(
            new ProfileSkill { Id = 100, CandidateProfileId = own.Id, MasterSkillId = 1, ProficiencyLevel = ProficiencyLevel.Intermediate, Source = ProfileSkillSource.Manual, ConfirmedAt = DateTimeOffset.UtcNow },
            new ProfileSkill { Id = 200, CandidateProfileId = other.Id, MasterSkillId = 1, ProficiencyLevel = ProficiencyLevel.Expert, Source = ProfileSkillSource.Manual, ConfirmedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var service = CreateService(db, new StubResolver());
        Assert.False(await service.RemoveSkillAsync(10, 200));
        Assert.True(await service.RemoveSkillAsync(10, 100));
        Assert.Single(await db.ProfileSkills.ToListAsync());
        Assert.Equal(other.Id, (await db.ProfileSkills.SingleAsync()).CandidateProfileId);
    }

    [Fact]
    public async Task TotalExperience_is_null_when_no_work_experience_is_recorded()
    {
        await using var db = CreateDb();
        var profile = AddProfile(db, 10);
        await db.SaveChangesAsync();
        var service = CreateService(db, new StubResolver());

        await service.RecalculateTotalExperienceAsync(10);

        Assert.Null((await db.CandidateProfiles.SingleAsync()).TotalExperienceYears);
    }

    [Fact]
    public async Task TotalExperience_sums_sequential_roles()
    {
        await using var db = CreateDb();
        var profile = AddProfile(db, 10);
        profile.WorkExperience.Add(new WorkExperienceEntry { CompanyName = "A", JobTitle = "Dev", StartDate = new DateOnly(2020, 1, 1), EndDate = new DateOnly(2022, 1, 1), IsCurrent = false });
        profile.WorkExperience.Add(new WorkExperienceEntry { CompanyName = "B", JobTitle = "Dev", StartDate = new DateOnly(2022, 1, 1), EndDate = new DateOnly(2024, 1, 1), IsCurrent = false });
        await db.SaveChangesAsync();
        var service = CreateService(db, new StubResolver());

        await service.RecalculateTotalExperienceAsync(10);

        Assert.Equal(4.0m, (await db.CandidateProfiles.SingleAsync()).TotalExperienceYears);
    }

    [Fact]
    public async Task TotalExperience_does_not_double_count_overlapping_roles()
    {
        await using var db = CreateDb();
        var profile = AddProfile(db, 10);
        profile.WorkExperience.Add(new WorkExperienceEntry { CompanyName = "A", JobTitle = "Dev", StartDate = new DateOnly(2020, 1, 1), EndDate = new DateOnly(2023, 1, 1), IsCurrent = false });
        profile.WorkExperience.Add(new WorkExperienceEntry { CompanyName = "B", JobTitle = "Dev", StartDate = new DateOnly(2022, 1, 1), EndDate = new DateOnly(2024, 1, 1), IsCurrent = false });
        await db.SaveChangesAsync();
        var service = CreateService(db, new StubResolver());

        await service.RecalculateTotalExperienceAsync(10);

        Assert.Equal(4.0m, (await db.CandidateProfiles.SingleAsync()).TotalExperienceYears);
    }

    [Fact]
    public async Task TotalExperience_treats_an_open_ended_role_as_running_to_today()
    {
        await using var db = CreateDb();
        var profile = AddProfile(db, 10);
        var start = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(-2));
        profile.WorkExperience.Add(new WorkExperienceEntry { CompanyName = "A", JobTitle = "Dev", StartDate = start, EndDate = null, IsCurrent = true });
        await db.SaveChangesAsync();
        var service = CreateService(db, new StubResolver());

        await service.RecalculateTotalExperienceAsync(10);

        var years = (await db.CandidateProfiles.SingleAsync()).TotalExperienceYears;
        Assert.NotNull(years);
        Assert.InRange(years!.Value, 1.99m, 2.01m);
    }

    private static CandidateProfile AddProfile(JobAlignDbContext db, int userId)
    {
        var profile = new CandidateProfile
        {
            UserId = userId,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.CandidateProfiles.Add(profile);
        return profile;
    }

    private static JobAlignDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<JobAlignDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new JobAlignDbContext(options);
    }

    private static CandidateProfileService CreateService(JobAlignDbContext db, ISkillResolver resolver) =>
        new(db, resolver, new EmptyServiceProvider(), NullLogger<CandidateProfileService>.Instance);

    private sealed class StubResolver : ISkillResolver
    {
        private readonly SkillResolution _resolution;
        public StubResolver(SkillResolution? resolution = null) => _resolution = resolution ?? new SkillResolution("", null, null);
        public Task<SkillResolution> ResolveAsync(string rawSkillText, CancellationToken cancellationToken = default) => Task.FromResult(_resolution with { RawText = rawSkillText });
        public Task<IReadOnlyList<SkillResolution>> ResolveManyAsync(IEnumerable<string> rawSkillTexts, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SkillResolution>>(rawSkillTexts.Select(x => _resolution with { RawText = x }).ToList());
        public string Normalize(string rawSkillText) => rawSkillText.Trim().ToLowerInvariant();
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type? serviceType) => null;
    }
}
