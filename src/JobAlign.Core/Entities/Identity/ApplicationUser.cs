using JobAlign.Core.Entities.Postings;
using JobAlign.Core.Entities.Profiles;
using Microsoft.AspNetCore.Identity;

namespace JobAlign.Core.Entities.Identity;

/// <summary>
/// A system user (FR-01 to FR-03). Credentials, hashing and lockout come from
/// ASP.NET Core Identity, which satisfies NFR-05 (salted one-way hash) without
/// hand-rolled password handling.
/// </summary>
public class ApplicationUser : IdentityUser<int>
{
    /// <summary>
    /// False when an administrator has deactivated the account (FR-55).
    /// Kept separate from Identity's LockoutEnd, which exists for failed-login
    /// lockout — an administrative deactivation is a different concept.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? DeactivatedAt { get; set; }

    /// <summary>
    /// The candidate profile, where the user holds the Candidate role.
    /// Administrators have no profile — and by BR-09 may not read one.
    /// </summary>
    public CandidateProfile? CandidateProfile { get; set; }

    /// <summary>Postings owned by this user. Ownership is the basis of BR-09.</summary>
    public ICollection<JobPosting> JobPostings { get; set; } = new List<JobPosting>();
}
