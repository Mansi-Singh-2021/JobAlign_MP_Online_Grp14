using JobAlign.Core.Entities.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JobAlign.Infrastructure.Identity;

/// <summary>
/// Creates a development administrator account so the FR-57/FR-58 screens are reachable.
/// </summary>
/// <remarks>
/// Public registration always produces a Candidate (FR-01, FR-03), and the screen for an
/// administrator to assign roles is FR-56, which is not built yet. Without this there is no
/// way to reach the skill administration screens at all.
///
/// **Runs in the Development environment only, and only when both settings are present.**
/// The environment check is not a convenience — a seeded account with a known password is a
/// back door anywhere else. When FR-55/FR-56 land, delete this and promote a real account.
///
/// The administrator gets no <c>CandidateProfile</c>: administrators manage accounts and
/// master data, and may not hold or read candidate data (BR-09, section 4.3).
/// </remarks>
public static class AdminSeeder
{
    public static async Task SeedAsync(
        IServiceProvider services,
        IConfiguration configuration,
        bool isDevelopment,
        CancellationToken cancellationToken = default)
    {
        // Passed in rather than read from IHostEnvironment: Infrastructure has no business
        // referencing the hosting stack, and the caller already knows.
        if (!isDevelopment)
            return;

        var email = configuration["Seed:AdminEmail"];
        var password = configuration["Seed:AdminPassword"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return;

        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AdminSeeder));

        if (await users.FindByEmailAsync(email) is not null)
            return;

        cancellationToken.ThrowIfCancellationRequested();

        var admin = new ApplicationUser
        {
            UserName = email,
            Email = email,
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true
        };

        var created = await users.CreateAsync(admin, password);

        if (!created.Succeeded)
        {
            var errors = string.Join("; ", created.Errors.Select(e => $"{e.Code}: {e.Description}"));
            logger.LogWarning("Development administrator was not created. {Errors}", errors);
            return;
        }

        var roled = await users.AddToRoleAsync(admin, RoleNames.Administrator);

        if (!roled.Succeeded)
        {
            var errors = string.Join("; ", roled.Errors.Select(e => $"{e.Code}: {e.Description}"));
            logger.LogWarning("Development administrator has no role. {Errors}", errors);
            return;
        }

        logger.LogWarning(
            "Created the DEVELOPMENT administrator {Email} from configuration. "
            + "This account exists only because the environment is Development — never enable Seed:AdminPassword elsewhere.",
            email);
    }
}
