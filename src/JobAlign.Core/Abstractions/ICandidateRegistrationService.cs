using Microsoft.AspNetCore.Identity;

namespace JobAlign.Core.Abstractions;

/// <summary>
/// Registers a new candidate account (FR-01).
/// </summary>
/// <remarks>
/// Registration is three writes that must all happen or none of them: the Identity
/// user, the Candidate role assignment (FR-03), and the candidate profile every
/// candidate is assumed to have. Keeping them behind one method means no caller can
/// create a user that is missing a role or a profile.
///
/// Registration always produces a Candidate. Administrator accounts are created by an
/// existing administrator (FR-56), never through the public form.
/// </remarks>
public interface ICandidateRegistrationService
{
    Task<IdentityResult> RegisterAsync(
        string email,
        string fullName,
        string password,
        CancellationToken cancellationToken = default);
}
