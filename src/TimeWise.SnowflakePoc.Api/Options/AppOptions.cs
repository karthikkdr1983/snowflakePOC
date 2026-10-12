using System.ComponentModel.DataAnnotations;

namespace TimeWise.SnowflakePoc.Api.Options;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";
    public const string TestHeaderMode = "TestHeader";

    /// <summary>"TestHeader" for Phase 1. Phase 2 adds "EntraId".</summary>
    [Required] public string Mode { get; set; } = TestHeaderMode;
}

public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    [Range(0, 3600)] public int TtlSeconds { get; set; } = 120;
}

public sealed class ResilienceOptions
{
    public const string SectionName = "Resilience";

    [Range(1, 120)] public int AttemptTimeoutSeconds { get; set; } = 15;
    [Range(0, 5)] public int MaxRetries { get; set; } = 2;
}
