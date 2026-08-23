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

        // TEMPORARY: Member B replaces this with SkillResolver in Wave 0.
        services.AddScoped<ISkillResolver, SkillResolver>();
        services.AddScoped<MasterSkillSeeder>();

        return services;
    }
}
