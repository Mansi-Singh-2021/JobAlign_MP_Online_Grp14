namespace JobAlign.Core.Entities.Skills;

/// <summary>
/// A normalized place, so that variations of the same location resolve to one
/// entry (FR-16). The posting always keeps the text it actually stated —
/// see PostingExtraction.RawLocationText — alongside this reference.
/// </summary>
public class Location
{
    public int Id { get; set; }

    /// <summary>Approved display form, e.g. "Bengaluru, Karnataka, India".</summary>
    public required string CanonicalName { get; set; }

    /// <summary>Lookup form of <see cref="CanonicalName"/>. Unique.</summary>
    public required string NormalizedName { get; set; }

    public string? City { get; set; }
    public string? Region { get; set; }
    public string? Country { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<LocationAlias> Aliases { get; set; } = new List<LocationAlias>();
}

/// <summary>
/// An alternative way of writing a location, e.g. "Bangalore" for "Bengaluru" (FR-16).
/// </summary>
public class LocationAlias
{
    public int Id { get; set; }

    public required string Alias { get; set; }

    /// <summary>Lookup form. Unique across the table.</summary>
    public required string NormalizedAlias { get; set; }

    public int LocationId { get; set; }
    public Location Location { get; set; } = null!;
}
