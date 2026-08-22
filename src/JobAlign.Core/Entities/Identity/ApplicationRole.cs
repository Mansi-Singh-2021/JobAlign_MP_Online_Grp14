using Microsoft.AspNetCore.Identity;

namespace JobAlign.Core.Entities.Identity;

/// <summary>
/// A role assignable to a user (FR-03, FR-56). The SRS defines exactly two:
/// see <see cref="RoleNames"/>.
/// </summary>
public class ApplicationRole : IdentityRole<int>
{
    public string? Description { get; set; }
}

/// <summary>
/// The only roles the system recognises (Section 4.3 role and permission matrix).
/// Referenced as constants so authorization attributes cannot drift from seed data.
/// </summary>
public static class RoleNames
{
    public const string Candidate = "Candidate";
    public const string Administrator = "Administrator";

    public static readonly IReadOnlyList<string> All = new[] { Candidate, Administrator };
}
