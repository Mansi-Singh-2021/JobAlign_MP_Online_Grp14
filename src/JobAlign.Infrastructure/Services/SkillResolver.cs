public class SkillResolver : ISkillResolver
{
    private readonly JobAlignDbContext _context;
    private readonly ILogger<SkillResolver> _logger;

    public SkillResolver(JobAlignDbContext context, ILogger<SkillResolver> logger)
    {
        _context = context;
        _logger = logger;
    }

    public string Normalize(string rawSkillText)
    {
        if (string.IsNullOrWhiteSpace(rawSkillText)) return string.Empty;

        var normalized = rawSkillText.Trim();

        // Special cases: # -> sharp, + -> plus
        normalized = normalized.Replace("#", "sharp");
        normalized = normalized.Replace("+", "plus");

        // Lowercase, keep only letters and digits
        normalized = new string(normalized
            .Where(char.IsLetterOrDigit)
            .ToArray())
            .ToLowerInvariant();

        return normalized;
    }

    public async Task<SkillResolution> ResolveAsync(string rawSkillText, CancellationToken cancellationToken = default)
    {
        // Implementation: normalize → query master skills → query aliases → follow merge chain
        // Return unresolved if not found (not exception)
    }

    public async Task<IReadOnlyList<SkillResolution>> ResolveManyAsync(
        IEnumerable<string> rawSkillTexts,
        CancellationToken cancellationToken = default)
    {
        // ONE database query: 
        // 1. Normalize all inputs
        // 2. Query all master skills WHERE NormalizedName IN (...)
        // 3. Query all aliases WHERE NormalizedAlias IN (...)
        // 4. Map results back preserving input order
        // 5. Follow merge chains
    }
}