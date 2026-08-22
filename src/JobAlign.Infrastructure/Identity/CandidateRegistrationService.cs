using JobAlign.Core.Abstractions;
using JobAlign.Core.Entities.Identity;
using JobAlign.Core.Entities.Profiles;
using JobAlign.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;

namespace JobAlign.Infrastructure.Identity;

/// <inheritdoc cref="ICandidateRegistrationService"/>
public class CandidateRegistrationService : ICandidateRegistrationService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly JobAlignDbContext _db;

    public CandidateRegistrationService(UserManager<ApplicationUser> users, JobAlignDbContext db)
    {
        _users = users;
        _db = db;
    }

    public async Task<IdentityResult> RegisterAsync(
        string email,
        string fullName,
        string password,
        CancellationToken cancellationToken = default)
    {
        // UserManager and the DbContext share a connection, so one transaction covers
        // the user, the role assignment and the profile.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true
        };

        // Hashing, the unique-email check and the password policy are all Identity's,
        // which is what satisfies NFR-05 without hand-rolled credential handling.
        var created = await _users.CreateAsync(user, password);
        if (!created.Succeeded)
            return created;

        var roled = await _users.AddToRoleAsync(user, RoleNames.Candidate);   // FR-03
        if (!roled.Succeeded)
            return roled;

        _db.CandidateProfiles.Add(new CandidateProfile
        {
            UserId = user.Id,
            FullName = fullName.Trim(),
            UpdatedAt = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return IdentityResult.Success;
    }
}
