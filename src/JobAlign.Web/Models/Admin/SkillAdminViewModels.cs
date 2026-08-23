using System.ComponentModel.DataAnnotations;

namespace JobAlign.Web.Models.Admin;

/// <summary>One row of the master skill list (FR-57).</summary>
public class SkillRowViewModel
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public required string NormalizedName { get; init; }
    public string? Category { get; init; }
    public bool IsActive { get; init; }
    public int AliasCount { get; init; }

    /// <summary>Set where this skill was merged away (FR-58); names the surviving skill.</summary>
    public string? MergedInto { get; init; }

    public bool IsMerged => MergedInto is not null;
}

/// <summary>The master skill list page (FR-57).</summary>
public class SkillListViewModel
{
    public IReadOnlyList<SkillRowViewModel> Skills { get; init; } = [];
    public string? Search { get; init; }
    public bool IncludeInactive { get; init; }
    public int ActiveCount { get; init; }
    public int TotalCount { get; init; }
}

/// <summary>Add or edit a master skill (FR-57).</summary>
public class EditSkillViewModel
{
    /// <summary>Zero when adding.</summary>
    public int Id { get; set; }

    [Required, StringLength(128)]
    [Display(Name = "Skill name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(64)]
    [Display(Name = "Category")]
    public string? Category { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsMerged { get; set; }
    public string? MergedInto { get; set; }

    public bool IsNew => Id == 0;

    /// <summary>What the name reduces to for lookup. Shown so the effect of a rename is visible.</summary>
    public string? NormalizedPreview { get; set; }
}

/// <summary>Manage one skill's aliases (FR-58).</summary>
public class SkillAliasesViewModel
{
    public int SkillId { get; init; }
    public required string SkillName { get; init; }
    public required string NormalizedName { get; init; }

    public IReadOnlyList<AliasRowViewModel> Aliases { get; init; } = [];

    [StringLength(128)]
    [Display(Name = "New alias")]
    public string? NewAlias { get; set; }
}

public class AliasRowViewModel
{
    public int Id { get; init; }
    public required string Alias { get; init; }
    public required string NormalizedAlias { get; init; }
}

/// <summary>Merge one skill into another (FR-58).</summary>
public class MergeSkillsViewModel
{
    [Display(Name = "Merge this skill away")]
    public int SourceId { get; set; }

    [Display(Name = "Into this skill")]
    public int TargetId { get; set; }

    /// <summary>Active, unmerged skills — the only valid merge targets.</summary>
    public IReadOnlyList<SkillRowViewModel> Candidates { get; init; } = [];
}
