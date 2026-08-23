using JobAlign.Core.Abstractions;
using JobAlign.Core.Entities.Postings;
using JobAlign.Core.Entities.Profiles;
using JobAlign.Core.Entities.Skills;
using JobAlign.Core.Enums;
using JobAlign.Infrastructure.Data;
using JobAlign.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobAlign.Tests;

/// <summary>
/// Administrator maintenance of master skills and aliases (FR-57, FR-58).
/// </summary>
public class SkillAdminServiceTests : IDisposable
{
    private readonly JobAlignDbContext _db;
    private readonly SkillAdminService _admin;

    public SkillAdminServiceTests()
    {
        var options = new DbContextOptionsBuilder<JobAlignDbContext>()
            .UseInMemoryDatabase($"skilladmin-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new JobAlignDbContext(options);

        var resolver = new SkillResolver(_db, NullLogger<SkillResolver>.Instance);
        _admin = new SkillAdminService(_db, resolver, NullLogger<SkillAdminService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    // ---------------------------------------------------------------- FR-57

    [Fact]
    public async Task Creates_a_skill_with_its_normalized_form()
    {
        var result = await _admin.CreateAsync("C#", "Languages");

        Assert.True(result.Succeeded);

        var skill = await _db.MasterSkills.SingleAsync();
        Assert.Equal("C#", skill.Name);
        Assert.Equal("csharp", skill.NormalizedName);   // must match what the resolver looks up
        Assert.True(skill.IsActive);
    }

    [Fact]
    public async Task Rejects_a_name_that_normalizes_onto_an_existing_skill()
    {
        await _admin.CreateAsync("C#", null);

        var result = await _admin.CreateAsync("c sharp", null);

        // Two skills reading the same way would give the resolver two answers for one
        // input, which BR-04 forbids.
        Assert.False(result.Succeeded);
        Assert.Contains("C#", result.Error);
        Assert.Single(await _db.MasterSkills.ToListAsync());
    }

    [Fact]
    public async Task Rejects_a_name_that_collides_with_an_existing_alias()
    {
        await _admin.CreateAsync("Kubernetes", null);
        var kubernetes = await _db.MasterSkills.SingleAsync();
        await _admin.AddAliasAsync(kubernetes.Id, "K8s");

        var result = await _admin.CreateAsync("K8S", null);

        Assert.False(result.Succeeded);
        Assert.Contains("Kubernetes", result.Error);
    }

    [Fact]
    public async Task Deactivates_rather_than_deletes()
    {
        await _admin.CreateAsync("Silverlight", null);
        var skill = await _db.MasterSkills.SingleAsync();

        var result = await _admin.SetActiveAsync(skill.Id, false);

        Assert.True(result.Succeeded);
        // The row survives: postings referencing it must stay explainable.
        Assert.False((await _db.MasterSkills.SingleAsync()).IsActive);
    }

    // ---------------------------------------------------------------- FR-58 aliases

    [Fact]
    public async Task Rejects_an_alias_that_reads_the_same_as_its_own_skill()
    {
        await _admin.CreateAsync("REST API", null);
        var skill = await _db.MasterSkills.SingleAsync();

        var result = await _admin.AddAliasAsync(skill.Id, "rest api");

        Assert.False(result.Succeeded);
        Assert.Empty(await _db.SkillAliases.ToListAsync());
    }

    [Fact]
    public async Task Rejects_an_alias_already_used_by_another_skill()
    {
        await _admin.CreateAsync("Kubernetes", null);
        await _admin.CreateAsync("Docker", null);
        var kubernetes = await _db.MasterSkills.FirstAsync(s => s.Name == "Kubernetes");
        var docker = await _db.MasterSkills.FirstAsync(s => s.Name == "Docker");
        await _admin.AddAliasAsync(kubernetes.Id, "K8s");

        var result = await _admin.AddAliasAsync(docker.Id, "k8s");

        Assert.False(result.Succeeded);
        Assert.Single(await _db.SkillAliases.ToListAsync());
    }

    // ---------------------------------------------------------------- FR-58 merge

    [Fact]
    public async Task Merge_keeps_the_source_row_and_points_it_at_the_target()
    {
        var (source, target) = await GivenTwoSkills("Kubernets", "Kubernetes");

        var result = await _admin.MergeAsync(source.Id, target.Id);

        Assert.True(result.Succeeded);

        var merged = await _db.MasterSkills.FirstAsync(s => s.Id == source.Id);
        Assert.Equal(target.Id, merged.MergedIntoMasterSkillId);
        Assert.False(merged.IsActive);
    }

    [Fact]
    public async Task Merge_turns_the_source_name_into_an_alias_of_the_target()
    {
        var (source, target) = await GivenTwoSkills("Kubernets", "Kubernetes");

        await _admin.MergeAsync(source.Id, target.Id);

        // Otherwise a posting still worded "Kubernets" would stop resolving after the merge.
        var alias = await _db.SkillAliases.SingleAsync(a => a.NormalizedAlias == "kubernets");
        Assert.Equal(target.Id, alias.MasterSkillId);
    }

    [Fact]
    public async Task Merge_moves_the_source_aliases_to_the_target()
    {
        var (source, target) = await GivenTwoSkills("Kubernets", "Kubernetes");
        await _admin.AddAliasAsync(source.Id, "Kube");

        await _admin.MergeAsync(source.Id, target.Id);

        var alias = await _db.SkillAliases.SingleAsync(a => a.NormalizedAlias == "kube");
        Assert.Equal(target.Id, alias.MasterSkillId);
    }

    [Fact]
    public async Task Merge_repoints_posting_skills_so_scoring_sees_one_skill()
    {
        var (source, target) = await GivenTwoSkills("Kubernets", "Kubernetes");

        _db.PostingSkills.Add(new PostingSkill
        {
            JobPostingId = 1, MasterSkillId = source.Id,
            SkillType = SkillType.Required, Source = PostingSkillSource.Extracted
        });
        await _db.SaveChangesAsync();

        await _admin.MergeAsync(source.Id, target.Id);

        // Scoring compares MasterSkillId directly. Leaving the row on the source would mean
        // a candidate holding the target still failed to match this posting.
        Assert.Equal(target.Id, (await _db.PostingSkills.SingleAsync()).MasterSkillId);
    }

    [Fact]
    public async Task Merge_drops_a_duplicate_rather_than_breaking_the_unique_index()
    {
        var (source, target) = await GivenTwoSkills("Kubernets", "Kubernetes");

        _db.ProfileSkills.AddRange(
            new ProfileSkill { CandidateProfileId = 1, MasterSkillId = source.Id, ProficiencyLevel = ProficiencyLevel.Beginner, Source = ProfileSkillSource.Manual },
            new ProfileSkill { CandidateProfileId = 1, MasterSkillId = target.Id, ProficiencyLevel = ProficiencyLevel.Expert, Source = ProfileSkillSource.Manual });
        await _db.SaveChangesAsync();

        await _admin.MergeAsync(source.Id, target.Id);

        // ProfileSkills is unique on (profile, master skill), so the candidate keeps one row.
        var remaining = await _db.ProfileSkills.SingleAsync();
        Assert.Equal(target.Id, remaining.MasterSkillId);
    }

    [Fact]
    public async Task Merge_refuses_a_skill_into_itself()
    {
        await _admin.CreateAsync("Docker", null);
        var docker = await _db.MasterSkills.SingleAsync();

        Assert.False((await _admin.MergeAsync(docker.Id, docker.Id)).Succeeded);
    }

    [Fact]
    public async Task Merge_refuses_a_target_that_was_itself_merged()
    {
        var (a, b) = await GivenTwoSkills("A", "B");
        await _admin.CreateAsync("C", null);
        var c = await _db.MasterSkills.FirstAsync(s => s.Name == "C");

        await _admin.MergeAsync(a.Id, b.Id);
        var result = await _admin.MergeAsync(c.Id, a.Id);

        // Chaining merges would make the resolver walk a chain, and a circular one forever.
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task A_merged_skill_cannot_be_reactivated_on_its_own()
    {
        var (source, target) = await GivenTwoSkills("Kubernets", "Kubernetes");
        await _admin.MergeAsync(source.Id, target.Id);

        Assert.False((await _admin.SetActiveAsync(source.Id, true)).Succeeded);
    }

    // ----------------------------------------------------------------

    private async Task<(MasterSkill Source, MasterSkill Target)> GivenTwoSkills(string source, string target)
    {
        await _admin.CreateAsync(source, null);
        await _admin.CreateAsync(target, null);

        return (await _db.MasterSkills.FirstAsync(s => s.Name == source),
                await _db.MasterSkills.FirstAsync(s => s.Name == target));
    }
}
