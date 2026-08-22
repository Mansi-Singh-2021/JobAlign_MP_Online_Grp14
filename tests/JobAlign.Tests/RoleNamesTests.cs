using JobAlign.Core.Entities.Identity;

namespace JobAlign.Tests;

/// <summary>
/// The role list is fixed by section 4.3 of the SRS (FR-03).
/// </summary>
public class RoleNamesTests
{
    [Fact]
    public void The_system_recognises_exactly_two_roles()
    {
        // RoleSeeder seeds from this list and [Authorize(Roles = ...)] reads the same
        // constants, so an accidental addition here would silently create a role that
        // nothing grants and nothing checks.
        Assert.Equal(new[] { RoleNames.Candidate, RoleNames.Administrator }, RoleNames.All);
    }

    [Fact]
    public void Role_names_match_the_strings_used_in_authorization()
    {
        Assert.Equal("Candidate", RoleNames.Candidate);
        Assert.Equal("Administrator", RoleNames.Administrator);
    }
}
