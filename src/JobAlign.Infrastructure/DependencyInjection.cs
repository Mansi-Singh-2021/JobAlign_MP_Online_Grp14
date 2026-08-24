using JobAlign.Core.Abstractions;
using JobAlign.Infrastructure.Data;
using JobAlign.Infrastructure.Extraction;
using JobAlign.Infrastructure.Identity;
using JobAlign.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobAlign.Infrastructure;

/// <summary>
/// One place where the infrastructure layer says what it provides, so hosts do not
/// hand-wire EF and the service implementations one by one (NFR-11).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddJobAlignInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<JobAlignDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddScoped<ICandidateRegistrationService, CandidateRegistrationService>();
        services.AddScoped<IPostingReferenceGenerator, PostingReferenceGenerator>();
        services.AddScoped<IJobPostingService, JobPostingService>();

        // Development delivery only — see LoggingEmailSender.
        services.AddScoped<IAppEmailSender, LoggingEmailSender>();

        // --- Role A: extraction (build order step 3) ---
        // StubExtractor until Member F lands AiExtractor behind the same interface (NFR-11).
        services.AddScoped<IJobExtractor, StubExtractor>();
        services.AddScoped<IExtractionService, ExtractionService>();

        // --- Role B: master skills, aliases, resolution (FR-14, FR-29, FR-57, FR-58) ---
        services.AddScoped<ISkillResolver, SkillResolver>();
        services.AddScoped<MasterSkillSeeder>();
        services.AddScoped<ISkillAdminService, SkillAdminService>();

        // --- Role C: candidate profile and skills (FR-27 to FR-29, FR-33, FR-34) ---
        // One instance serves both seams, so a single unit of work covers a profile change
        // and the experience recalculation it triggers.
        services.AddScoped<CandidateProfileService>();
        services.AddScoped<ICandidateProfileService>(sp => sp.GetRequiredService<CandidateProfileService>());
        services.AddScoped<IProfileEntryService>(sp => sp.GetRequiredService<CandidateProfileService>());

        return services;
    }
}
