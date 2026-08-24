using JobAlign.Core.Abstractions;
using JobAlign.Infrastructure;
using JobAlign.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace JobAlign.Tests;

/// <summary>
/// AddJobAlignInfrastructure is the only place the hosts learn what the layer provides
/// (NFR-11). A service that compiles but is never registered fails at request time, not
/// at build time, so the container is asserted here instead.
/// </summary>
public class InfrastructureRegistrationTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // Never opened — resolution constructs the DbContext, it does not connect.
        services.AddJobAlignInfrastructure("Server=(local);Database=JobAlign;Trusted_Connection=True;");
        return services.BuildServiceProvider(validateScopes: true);
    }

    // ICandidateRegistrationService is deliberately absent: it depends on Identity's
    // UserManager, which the web host registers, not this layer.
    [Theory]
    [InlineData(typeof(ICandidateProfileService))]
    [InlineData(typeof(IProfileEntryService))]
    [InlineData(typeof(IJobPostingService))]
    [InlineData(typeof(IExtractionService))]
    [InlineData(typeof(ISkillResolver))]
    [InlineData(typeof(ISkillAdminService))]
    [InlineData(typeof(IMatchScoringService))]
    public void Every_contract_a_controller_asks_for_resolves(Type contract)
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService(contract));
    }

    /// <summary>
    /// CandidateProfileService reaches match scoring by reflection, because Role C was
    /// written before Role D existed. A rename on either side makes Type.GetType return
    /// null, and the rescore then silently stops happening: no exception, no failing test,
    /// FR-41 quietly broken. These assertions are what make that rename loud.
    /// </summary>
    [Fact]
    public void The_reflected_scoring_hook_still_finds_its_target()
    {
        var contract = Type.GetType("JobAlign.Core.Abstractions.IMatchScoringService, JobAlign.Core");
        Assert.NotNull(contract);

        var method = contract!.GetMethod("RecalculateAllAsync");
        Assert.NotNull(method);
        Assert.Equal(
            [typeof(int), typeof(CancellationToken)],
            method!.GetParameters().Select(p => p.ParameterType));
        Assert.True(typeof(Task).IsAssignableFrom(method.ReturnType));

        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService(contract));
    }

    [Fact]
    public void The_two_profile_seams_share_one_instance_per_scope()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var profiles = scope.ServiceProvider.GetRequiredService<ICandidateProfileService>();
        var entries = scope.ServiceProvider.GetRequiredService<IProfileEntryService>();

        // AddWorkExperienceAsync calls RecalculateTotalExperienceAsync across the two seams;
        // separate instances would split that across two units of work.
        Assert.Same(profiles, entries);
        Assert.IsType<CandidateProfileService>(profiles);
    }
}
