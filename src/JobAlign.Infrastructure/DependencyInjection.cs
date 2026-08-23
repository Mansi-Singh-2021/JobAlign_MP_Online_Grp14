using JobAlign.Core.Abstractions;
using JobAlign.Infrastructure.Data;
using JobAlign.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobAlign.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddJobAlignInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        // DbContext
        services.AddDbContext<JobAlignDbContext>(options =>
            options.UseSqlServer(connectionString));

        // Register repositories and services
        services.AddScoped<IJobPostingRepository, JobPostingRepository>();
        // ... other repositories ...

        // ⬇️ ADD THESE TWO LINES ⬇️
        services.AddScoped<ISkillResolver, SkillResolver>();
        services.AddScoped<MasterSkillSeeder>();

        return services;
    }
}
