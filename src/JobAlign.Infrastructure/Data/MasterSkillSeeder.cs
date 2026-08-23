using JobAlign.Core.Abstractions;
using JobAlign.Core.Entities.Skills;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobAlign.Infrastructure.Data;

/// <summary>
/// Seeds the master skill list and its aliases (FR-14, FR-57, FR-58).
/// </summary>
/// <remarks>
/// Idempotent, and safe to run on every startup: existing skills are left alone and only
/// missing aliases are added, so extending the list below is enough to roll it out.
///
/// Normalization comes from <see cref="ISkillResolver.Normalize"/> rather than a private copy.
/// If seeding and lookup ever normalized differently, every resolution would silently fail
/// and nothing would report an error — the whole master-skill mechanism would just stop
/// working. Sharing the one method is what prevents that.
///
/// Category is a plain string column on <see cref="MasterSkill"/>. There is no separate
/// category table in this schema.
/// </remarks>
public class MasterSkillSeeder
{
    private readonly JobAlignDbContext _context;
    private readonly ISkillResolver _resolver;
    private readonly ILogger<MasterSkillSeeder> _logger;

    public MasterSkillSeeder(
        JobAlignDbContext context,
        ISkillResolver resolver,
        ILogger<MasterSkillSeeder> logger)
    {
        _context = context;
        _resolver = resolver;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var existingSkills = await _context.MasterSkills
            .ToDictionaryAsync(s => s.NormalizedName, cancellationToken);

        var takenAliases = await _context.SkillAliases
            .Select(a => a.NormalizedAlias)
            .ToListAsync(cancellationToken);

        var reservedAliases = takenAliases.ToHashSet(StringComparer.Ordinal);

        var addedSkills = 0;
        var addedAliases = 0;

        foreach (var (name, category, aliases) in SkillData)
        {
            var normalizedName = _resolver.Normalize(name);

            if (!existingSkills.TryGetValue(normalizedName, out var skill))
            {
                skill = new MasterSkill
                {
                    Name = name,
                    NormalizedName = normalizedName,
                    Category = category,
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                _context.MasterSkills.Add(skill);
                existingSkills[normalizedName] = skill;
                addedSkills++;
            }

            foreach (var alias in aliases)
            {
                var normalizedAlias = _resolver.Normalize(alias);

                // Skip an alias that normalizes to nothing, that duplicates one already
                // reserved, or that collapses onto the skill's own canonical form. The
                // unique index on NormalizedAlias would otherwise reject the whole batch —
                // several aliases here legitimately collapse together, for example
                // "ASP .NET Core", "AspNet Core" and "Aspnetcore" all give "aspnetcore".
                if (normalizedAlias.Length == 0
                    || normalizedAlias == normalizedName
                    || !reservedAliases.Add(normalizedAlias))
                {
                    continue;
                }

                _context.SkillAliases.Add(new SkillAlias
                {
                    Alias = alias,
                    NormalizedAlias = normalizedAlias,
                    MasterSkill = skill,          // set by reference: a new skill has no Id yet
                    CreatedAt = DateTimeOffset.UtcNow
                });

                addedAliases++;
            }
        }

        if (addedSkills == 0 && addedAliases == 0)
        {
            _logger.LogInformation("Master skills already seeded; nothing to add.");
            return;
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Seeded {SkillCount} master skill(s) and {AliasCount} alias(es).",
            addedSkills, addedAliases);
    }

    /// <summary>
    /// The canonical list. A name added here must be spelled exactly as it should display —
    /// it is what candidates see, and what StubExtractor emits so extracted skills resolve.
    /// </summary>
    private static readonly (string Name, string Category, string[] Aliases)[] SkillData =
    [
        // ---- Languages ----
        ("C#",                    "Languages",  ["C Sharp", "C-Sharp", ".NET C#"]),
        ("Java",                  "Languages",  ["J2EE"]),
        ("Python",                "Languages",  ["Py"]),
        ("JavaScript",            "Languages",  ["JS"]),
        ("TypeScript",            "Languages",  ["TS"]),
        ("SQL",                   "Languages",  ["Structured Query Language"]),
        ("Go",                    "Languages",  ["Golang"]),
        ("C++",                   "Languages",  ["CPP"]),
        ("Kotlin",                "Languages",  []),
        ("PHP",                   "Languages",  []),
        ("Ruby",                  "Languages",  []),

        // ---- Frameworks ----
        ("ASP.NET Core",          "Frameworks", ["ASP.NET", "ASP .NET Core", "AspNet Core"]),
        (".NET",                  "Frameworks", ["DotNet"]),
        ("Entity Framework Core", "Frameworks", ["EF Core", "EntityFramework"]),
        ("React",                 "Frameworks", ["React.js"]),
        ("Angular",               "Frameworks", []),
        ("Vue",                   "Frameworks", ["Vue.js"]),
        ("Node.js",               "Frameworks", ["Node"]),
        ("Spring Boot",           "Frameworks", ["Spring"]),
        ("Django",                "Frameworks", []),
        ("Flask",                 "Frameworks", []),

        // ---- Cloud and DevOps ----
        ("Azure",                 "Cloud",      ["MS Azure"]),
        ("AWS",                   "Cloud",      ["Amazon Web Services"]),
        ("GCP",                   "Cloud",      ["Google Cloud", "Google Cloud Platform"]),
        ("Docker",                "Cloud",      []),
        ("Kubernetes",            "Cloud",      ["K8s"]),
        ("Terraform",             "Cloud",      []),

        // ---- Data ----
        ("SQL Server",            "Data",       ["MSSQL", "MS SQL Server", "Microsoft SQL Server"]),
        ("PostgreSQL",            "Data",       ["Postgres"]),
        ("MySQL",                 "Data",       []),
        ("MongoDB",               "Data",       []),
        ("Redis",                 "Data",       []),
        ("Power BI",              "Data",       ["PowerBI"]),

        // ---- Practices ----
        ("REST API",              "Practice",   ["REST", "RESTful API", "RESTful"]),
        ("Microservices",         "Practice",   []),
        ("CI/CD",                 "Practice",   ["CI CD", "Continuous Integration"]),
        ("Git",                   "Practice",   []),
        ("Agile",                 "Practice",   []),
        ("Scrum",                 "Practice",   []),
        ("Unit Testing",          "Practice",   ["Unit Test"]),
        ("TDD",                   "Practice",   ["Test Driven Development"]),

        // ---- Tools ----
        ("Visual Studio",         "Tools",      ["VS"]),
        ("Azure DevOps",          "Tools",      ["VSTS"]),
        ("Jira",                  "Tools",      []),
        ("Jenkins",               "Tools",      []),
        ("GitHub Actions",        "Tools",      ["GH Actions", "GitHub CI"])
    ];
}
