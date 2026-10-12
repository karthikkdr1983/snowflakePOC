using System.ComponentModel.DataAnnotations;

namespace TimeWise.SnowflakePoc.Api.Options;

/// <summary>
/// Connection settings for the Snowflake service account.
/// When moving from the trial to the client's account, only these values change.
/// </summary>
public sealed class SnowflakeOptions
{
    public const string SectionName = "Snowflake";

    /// <summary>Account identifier, e.g. ABCDEFG-XY12345 (ORGNAME-ACCOUNTNAME).</summary>
    [Required(ErrorMessage = "Snowflake:Account is not set. Run: dotnet user-secrets set \"Snowflake:Account\" \"ORGNAME-ACCOUNTNAME\" " +
                             "--project src\\TimeWise.SnowflakePoc.Api (or set it in appsettings.Development.json and run with ASPNETCORE_ENVIRONMENT=Development).")]
    public string Account { get; set; } = "";

    /// <summary>Optional host override (e.g. a PrivateLink host). Leave empty to derive from Account.</summary>
    public string? Host { get; set; }

    [Required] public string User { get; set; } = "";
    [Required] public string Role { get; set; } = "";
    [Required] public string Warehouse { get; set; } = "";
    [Required] public string Database { get; set; } = "";
    [Required] public string Schema { get; set; } = "";

    /// <summary>Path to the encrypted PKCS#8 private key. Relative paths resolve from the API's content root.</summary>
    [Required] public string PrivateKeyPath { get; set; } = "";

    /// <summary>Passphrase for the private key. Keep it in user-secrets or Key Vault, never in appsettings.json.</summary>
    public string? PrivateKeyPassphrase { get; set; }

    /// <summary>Prefix for QUERY_TAG so API queries can be found in Snowflake query history.</summary>
    [Required] public string QueryTagPrefix { get; set; } = "timewise-api";

    [Range(5, 300)] public int CommandTimeoutSeconds { get; set; } = 30;
}
