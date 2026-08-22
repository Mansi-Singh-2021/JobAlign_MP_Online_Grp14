using JobAlign.Core.Entities.Identity;

namespace JobAlign.Core.Entities.Admin;

/// <summary>
/// A record of an administrative change to users, roles or master data (FR-60).
///
/// Append-only: rows are written and never updated or deleted, which is what
/// makes the log worth having. The actor is nullable so the entry survives the
/// deletion of the account that made the change.
/// </summary>
public class AuditEntry
{
    public int Id { get; set; }

    /// <summary>Who performed the action. Null where that account no longer exists.</summary>
    public int? ActorUserId { get; set; }
    public ApplicationUser? ActorUser { get; set; }

    /// <summary>What was done, e.g. "MasterSkill.Merged", "User.Deactivated".</summary>
    public required string Action { get; set; }

    /// <summary>The entity type affected, e.g. "MasterSkill".</summary>
    public required string EntityName { get; set; }

    /// <summary>Key of the affected record, as text so any key type is accommodated.</summary>
    public string? EntityId { get; set; }

    /// <summary>Optional detail, such as before/after values, as JSON.</summary>
    public string? Details { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
