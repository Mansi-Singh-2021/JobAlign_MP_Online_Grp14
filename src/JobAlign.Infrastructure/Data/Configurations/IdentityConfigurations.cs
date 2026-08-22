using JobAlign.Core.Entities.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobAlign.Infrastructure.Data.Configurations;

/// <summary>
/// Users (FR-01 to FR-03, FR-55). Password hashing, lockout and the unique
/// email index come from Identity itself, which is how NFR-05 is met without
/// bespoke credential handling.
/// </summary>
public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        // FR-01 requires the email itself to be unique, not merely the username.
        builder.HasIndex(x => x.NormalizedEmail)
            .IsUnique()
            .HasDatabaseName("UX_AspNetUsers_NormalizedEmail")
            .HasFilter("[NormalizedEmail] IS NOT NULL");

        builder.HasOne(x => x.CandidateProfile)
            .WithOne(x => x.User)
            .HasForeignKey<Core.Entities.Profiles.CandidateProfile>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Roles (FR-03, FR-56). Only Candidate and Administrator exist.</summary>
public class ApplicationRoleConfiguration : IEntityTypeConfiguration<ApplicationRole>
{
    public void Configure(EntityTypeBuilder<ApplicationRole> builder)
    {
        builder.Property(x => x.Description).HasMaxLength(256);
    }
}
