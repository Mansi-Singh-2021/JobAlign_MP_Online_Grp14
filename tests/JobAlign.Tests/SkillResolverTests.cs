using JobAlign.Core.Abstractions;
using JobAlign.Core.Entities.Skills;
using JobAlign.Infrastructure.Data;
using JobAlign.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace JobAlign.Tests;

public class SkillResolverTests
{
    private readonly JobAlignDbContext _context;
    private readonly SkillResolver _resolver;
    private readonly Mock<ILogger<SkillResolver>> _loggerMock;

    public SkillResolverTests()
    {
        var options = new DbContextOptionsBuilder<JobAlignDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new JobAlignDbContext(options);
        _loggerMock = new Mock<ILogger<SkillResolver>>();
        _resolver = new SkillResolver(_context, _loggerMock.Object);
    }

    [Fact]
    public void Normalize_maps_csharp_variants_to_one_form()
    {
        Assert.Equal("csharp", _resolver.Normalize("C#"));
        Assert.Equal("csharp", _resolver.Normalize("C Sharp"));
        Assert.Equal("csharp", _resolver.Normalize("C-Sharp"));
        Assert.Equal("csharp", _resolver.Normalize("c sharp"));
    }

    [Fact]
    public void Normalize_maps_hash_to_sharp_and_plus_to_plus()
    {
        Assert.Equal("csharp", _resolver.Normalize("C#"));
        Assert.Equal("cplusplus", _resolver.Normalize("C++"));
    }

    [Fact]
    public void Normalize_strips_punctuation_and_whitespace()
    {
        Assert.Equal("aspnetcore", _resolver.Normalize("ASP .NET Core"));
        Assert.Equal("react", _resolver.Normalize("  react  "));
        Assert.Equal("nodejs", _resolver.Normalize("Node.js"));
    }

    [Fact]
    public async Task Resolve_finds_a_skill_by_its_canonical_name()
    {
        // Seed a skill
        var skill = new MasterSkill
        {
            Name = "C#",
            NormalizedName = "csharp",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.MasterSkills.Add(skill);
        await _context.SaveChangesAsync();

        var result = await _resolver.ResolveAsync("C#");

        Assert.True(result.IsResolved);
        Assert.Equal(skill.Id, result.MasterSkillId);
        Assert.Equal("C#", result.CanonicalName);
    }

    [Fact]
    public async Task Resolve_finds_a_skill_by_an_alias()
    {
        var skill = new MasterSkill
        {
            Name = "Kubernetes",
            NormalizedName = "kubernetes",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.MasterSkills.Add(skill);
        await _context.SaveChangesAsync();

        var alias = new SkillAlias
        {
            MasterSkillId = skill.Id,
            Alias = "K8s",
            NormalizedAlias = "k8s"
        };
        _context.SkillAliases.Add(alias);
        await _context.SaveChangesAsync();

        var result = await _resolver.ResolveAsync("K8s");

        Assert.True(result.IsResolved);
        Assert.Equal(skill.Id, result.MasterSkillId);
        Assert.Equal("Kubernetes", result.CanonicalName);
    }

    [Fact]
    public async Task Resolve_follows_a_merged_skill_to_its_target()
    {
        // Source skill
        var source = new MasterSkill
        {
            Name = "OldName",
            NormalizedName = "oldname",
            IsActive = false,
            CreatedAt = DateTimeOffset.UtcNow,
            MergedIntoMasterSkillId = 2 // Will set after target created
        };
        _context.MasterSkills.Add(source);
        await _context.SaveChangesAsync();

        // Target skill
        var target = new MasterSkill
        {
            Name = "NewName",
            NormalizedName = "newname",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.MasterSkills.Add(target);
        await _context.SaveChangesAsync();

        // Update source with target ID
        source.MergedIntoMasterSkillId = target.Id;
        _context.MasterSkills.Update(source);
        await _context.SaveChangesAsync();

        var result = await _resolver.ResolveAsync("OldName");

        Assert.True(result.IsResolved);
        Assert.Equal(target.Id, result.MasterSkillId);
        Assert.Equal("NewName", result.CanonicalName);
    }

    [Fact]
    public async Task Resolve_returns_unresolved_for_an_unknown_skill()
    {
        var result = await _resolver.ResolveAsync("UnknownSkillName");

        Assert.False(result.IsResolved);
        Assert.Null(result.MasterSkillId);
        Assert.Null(result.CanonicalName);
        Assert.Equal("UnknownSkillName", result.RawText);
    }

    [Fact]
    public async Task Resolve_ignores_a_deactivated_skill()
    {
        var skill = new MasterSkill
        {
            Name = "Deactivated",
            NormalizedName = "deactivated",
            IsActive = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.MasterSkills.Add(skill);
        await _context.SaveChangesAsync();

        var result = await _resolver.ResolveAsync("Deactivated");

        Assert.False(result.IsResolved);
    }

    [Fact]
    public async Task ResolveMany_returns_one_result_per_input_in_order()
    {
        // Seed a skill
        var skill = new MasterSkill
        {
            Name = "C#",
            NormalizedName = "csharp",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.MasterSkills.Add(skill);
        await _context.SaveChangesAsync();

        var inputs = new[] { "C#", "Unknown", "C Sharp" };
        var results = await _resolver.ResolveManyAsync(inputs);

        Assert.Equal(3, results.Count);
        Assert.True(results[0].IsResolved); // C#
        Assert.False(results[1].IsResolved); // Unknown
        Assert.True(results[2].IsResolved); // C Sharp resolves too
    }
}