using JobAlign.Core.Entities.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JobAlign.Infrastructure.Identity;

/// <summary>
/// Creates the two roles the SRS defines, if they are not already present (FR-03).
/// </summary>
/// <remarks>
/// Section 4.3 fixes the role list at Candidate and Administrator. Seeding from
/// <see cref="RoleNames.All"/> rather than a literal list here means the constants
/// used by <c>[Authorize(Roles = ...)]</c> and the seeded rows cannot drift apart.
/// Idempotent: safe to run on every startup.
/// </remarks>
public static class RoleSeeder
{
    private static readonly IReadOnlyDictionary<string, string> Descriptions =
        new Dictionary<string, string>
        {
            [RoleNames.Candidate] =
                "Captures and manages their own postings, profile and resume; sees their own scores and roadmap.",
            [RoleNames.Administrator] =
                "Manages users, roles, the master skill list and extraction settings. May not read candidate postings or resumes (BR-09)."
        };

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(RoleSeeder));

        foreach (var roleName in RoleNames.All)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await roleManager.RoleExistsAsync(roleName))
                continue;

            var role = new ApplicationRole
            {
                Name = roleName,
                Description = Descriptions.TryGetValue(roleName, out var d) ? d : null
            };

            var result = await roleManager.CreateAsync(role);

            if (result.Succeeded)
            {
                logger.LogInformation("Seeded role {RoleName}.", roleName);
            }
            else
            {
                // A missing role means authorization silently denies everything, so this
                // is worth failing startup over rather than logging and carrying on.
                var errors = string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));
                throw new InvalidOperationException($"Could not seed role '{roleName}'. {errors}");
            }
        }
    }
}
