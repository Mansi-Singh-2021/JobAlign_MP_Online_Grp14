using JobAlign.Core.Abstractions;
using JobAlign.Infrastructure.Ai;
using JobAlign.Infrastructure.Data;
using JobAlign.Infrastructure.Extraction;
using JobAlign.Infrastructure.Identity;
using JobAlign.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

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
        services.AddScoped<StubExtractor>();
        services.AddScoped<AiExtractor>();

        // Role F, build order step 4: swaps to AiExtractor once an API key is configured.
        // Falls back to the stub when no key is present so the app still runs (NFR-06,
        // role-f handout "Day-2 swap") — announced to the team before this line changed.
        services.AddScoped<IJobExtractor>(sp =>
            string.IsNullOrWhiteSpace(sp.GetRequiredService<IOptions<AiClientOptions>>().Value.ApiKey)
                ? sp.GetRequiredService<StubExtractor>()
                : sp.GetRequiredService<AiExtractor>());

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

        // --- Role D: confirmed-posting match scoring (FR-35 to FR-43) ---
        // Registered after Role C: a profile change rescores the library (FR-41).
        services.AddScoped<IMatchScoringService, MatchScoringService>();

        // --- Role E: skill gaps, roadmap and dashboard (FR-42 to FR-54) ---
        services.AddScoped<ISkillGapService, SkillGapService>();

        // --- Role F: AI client — extraction (above) and feedback (FR-44, FR-48, NFR-09, NFR-11) ---
        // IConfiguration is resolved optionally, not required. The web host always registers
        // it, but this method's only stated dependency is the connection string, and a bare
        // ServiceCollection (which is what the registration tests build) has no IConfiguration.
        // Requiring it made every contract below IExtractionService fail to resolve in tests
        // while working fine in the app — the worst shape of a bug. Absent configuration, the
        // property defaults on AiClientOptions stand and ApiKey stays null, so the stub
        // implementations are selected exactly as they are for a teammate with no key.
        services.AddOptions<AiClientOptions>()
            .Configure<IServiceProvider>((options, serviceProvider) =>
                serviceProvider.GetService<IConfiguration>()
                    ?.GetSection(AiClientOptions.SectionName)
                    .Bind(options));

        services.AddHttpClient<AnthropicClient>();

        services.AddScoped<StubFeedbackGenerator>();
        services.AddScoped<AiFeedbackGenerator>();

        // Same no-key fallback as IJobExtractor above — nobody is blocked on an API key.
        services.AddScoped<IFeedbackGenerator>(sp =>
            string.IsNullOrWhiteSpace(sp.GetRequiredService<IOptions<AiClientOptions>>().Value.ApiKey)
                ? sp.GetRequiredService<StubFeedbackGenerator>()
                : sp.GetRequiredService<AiFeedbackGenerator>());

        return services;
    }
}
