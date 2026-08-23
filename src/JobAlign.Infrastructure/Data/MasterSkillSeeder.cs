using JobAlign.Core.Entities.Skills;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JobAlign.Infrastructure.Data;

public class MasterSkillSeeder
{
    private readonly JobAlignDbContext _context;
    private readonly ILogger<MasterSkillSeeder> _logger;

    public MasterSkillSeeder(JobAlignDbContext context, ILogger<MasterSkillSeeder> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        _logger.LogInformation("Seeding master skills and aliases...");

        // Step 1: Seed categories
        var categories = new Dictionary<string, SkillCategory>();
        var categoryNames = new[] { "Languages", "Frameworks", "Cloud", "Data", "Practice", "Tools" };

        foreach (var name in categoryNames)
        {
            var existing = await _context.SkillCategories
                .FirstOrDefaultAsync(c => c.CategoryName == name);

            if (existing == null)
            {
                var category = new SkillCategory { CategoryName = name };
                _context.SkillCategories.Add(category);
                categories[name] = category;
                _logger.LogDebug("Created category: {CategoryName}", name);
            }
            else
            {
                categories[name] = existing;
                _logger.LogDebug("Category already exists: {CategoryName}", name);
            }
        }

        await _context.SaveChangesAsync();

        // Step 2: Seed skills with their aliases
        var skillsData = GetSkillsData(categories);

        foreach (var (skillName, categoryId, aliases) in skillsData)
        {
            var normalizedName = NormalizeForSeeder(skillName);

            var existingSkill = await _context.MasterSkills
                .FirstOrDefaultAsync(s => s.NormalizedName == normalizedName);

            if (existingSkill == null)
            {
                var skill = new MasterSkill
                {
                    SkillName = skillName,
                    NormalizedName = normalizedName,
                    CategoryId = categoryId,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                _context.MasterSkills.Add(skill);
                await _context.SaveChangesAsync();

                // Add aliases
                foreach (var alias in aliases)
                {
                    var normalizedAlias = NormalizeForSeeder(alias);
                    var existingAlias = await _context.SkillAliases
                        .FirstOrDefaultAsync(a => a.NormalizedAlias == normalizedAlias);

                    if (existingAlias == null)
                    {
                        _context.SkillAliases.Add(new SkillAlias
                        {
                            SkillId = skill.Id,
                            AliasName = alias,
                            NormalizedAlias = normalizedAlias
                        });
                        _logger.LogDebug("Added alias '{Alias}' → {SkillName}", alias, skillName);
                    }
                }
                await _context.SaveChangesAsync();
                _logger.LogDebug("Seeded skill: {SkillName}", skillName);
            }
            else
            {
                _logger.LogDebug("Skill already exists: {SkillName}", skillName);
            }
        }

        _logger.LogInformation("Master skills seeding complete.");
    }

    private string NormalizeForSeeder(string raw)
    {
        // Same normalization as SkillResolver
        var normalized = raw.Trim();
        normalized = normalized.Replace("#", "sharp");
        normalized = normalized.Replace("+", "plus");
        normalized = new string(normalized
            .Where(char.IsLetterOrDigit)
            .ToArray())
            .ToLowerInvariant();
        return normalized;
    }

    private List<(string Name, int? CategoryId, List<string> Aliases)> GetSkillsData(
        Dictionary<string, SkillCategory> categories)
    {
        return new List<(string, int?, List<string>)>
        {
            // Languages
            ("C#", categories["Languages"]?.Id, new List<string> { "C Sharp", "C-Sharp", ".NET C#" }),
            ("Java", categories["Languages"]?.Id, new List<string> { "J2EE" }),
            ("Python", categories["Languages"]?.Id, new List<string> { "Py" }),
            ("JavaScript", categories["Languages"]?.Id, new List<string> { "JS" }),
            ("TypeScript", categories["Languages"]?.Id, new List<string> { "TS" }),
            ("SQL", categories["Languages"]?.Id, new List<string> { "Structured Query Language" }),
            ("Go", categories["Languages"]?.Id, new List<string> { "Golang" }),
            ("C++", categories["Languages"]?.Id, new List<string> { "Cplusplus", "CPP" }),
            ("Kotlin", categories["Languages"]?.Id, new List<string>()),
            ("PHP", categories["Languages"]?.Id, new List<string>()),
            ("Ruby", categories["Languages"]?.Id, new List<string>()),

            // Frameworks
            ("ASP.NET Core", categories["Frameworks"]?.Id, new List<string> { "ASP.NET", "ASP .NET Core", "AspNet Core", "Aspnetcore" }),
            (".NET", categories["Frameworks"]?.Id, new List<string> { "DotNet" }),
            ("Entity Framework Core", categories["Frameworks"]?.Id, new List<string> { "EF Core", "EntityFramework" }),
            ("React", categories["Frameworks"]?.Id, new List<string> { "React.js" }),
            ("Angular", categories["Frameworks"]?.Id, new List<string>()),
            ("Vue", categories["Frameworks"]?.Id, new List<string> { "Vue.js" }),
            ("Node.js", categories["Frameworks"]?.Id, new List<string> { "Node" }),
            ("Spring Boot", categories["Frameworks"]?.Id, new List<string> { "Spring" }),
            ("Django", categories["Frameworks"]?.Id, new List<string>()),
            ("Flask", categories["Frameworks"]?.Id, new List<string>()),

            // Cloud & DevOps
            ("Azure", categories["Cloud"]?.Id, new List<string> { "MS Azure" }),
            ("AWS", categories["Cloud"]?.Id, new List<string> { "Amazon Web Services" }),
            ("GCP", categories["Cloud"]?.Id, new List<string> { "Google Cloud", "Google Cloud Platform" }),
            ("Docker", categories["Cloud"]?.Id, new List<string>()),
            ("Kubernetes", categories["Cloud"]?.Id, new List<string> { "K8s" }),
            ("Terraform", categories["Cloud"]?.Id, new List<string>()),

            // Data
            ("SQL Server", categories["Data"]?.Id, new List<string> { "MSSQL", "MS SQL Server", "Microsoft SQL Server" }),
            ("PostgreSQL", categories["Data"]?.Id, new List<string> { "Postgres" }),
            ("MySQL", categories["Data"]?.Id, new List<string>()),
            ("MongoDB", categories["Data"]?.Id, new List<string>()),
            ("Redis", categories["Data"]?.Id, new List<string>()),
            ("Power BI", categories["Data"]?.Id, new List<string> { "PowerBI" }),

            // Practices
            ("REST API", categories["Practice"]?.Id, new List<string> { "REST", "RESTful API", "RESTful" }),
            ("Microservices", categories["Practice"]?.Id, new List<string>()),
            ("CI/CD", categories["Practice"]?.Id, new List<string> { "CI CD", "Continuous Integration" }),
            ("Git", categories["Practice"]?.Id, new List<string>()),
            ("Agile", categories["Practice"]?.Id, new List<string>()),
            ("Scrum", categories["Practice"]?.Id, new List<string>()),
            ("Unit Testing", categories["Practice"]?.Id, new List<string> { "Unit Test" }),
            ("TDD", categories["Practice"]?.Id, new List<string> { "Test Driven Development" }),

            // Tools
            ("Visual Studio", categories["Tools"]?.Id, new List<string> { "VS" }),
            ("Azure DevOps", categories["Tools"]?.Id, new List<string> { "Azure Devops", "VSTS" }),
            ("Jira", categories["Tools"]?.Id, new List<string>()),
            ("Jenkins", categories["Tools"]?.Id, new List<string>()),
            ("GitHub Actions", categories["Tools"]?.Id, new List<string> { "GH Actions", "GitHub CI" }),
        };
    }
}