using System.Text;
using TimeWise.SnowflakePoc.Api.Options;

namespace TimeWise.SnowflakePoc.Api.Data;

/// <summary>Builds the Snowflake.Data connection string for key-pair (JWT) authentication.</summary>
public static class SnowflakeConnectionString
{
    public const string ApplicationName = "TimeWise.SnowflakePoc";

    public static string Build(SnowflakeOptions options, string resolvedPrivateKeyPath)
    {
        var sb = new StringBuilder();
        Append(sb, "account", options.Account);
        if (!string.IsNullOrWhiteSpace(options.Host))
            Append(sb, "host", options.Host);
        Append(sb, "user", options.User);
        Append(sb, "role", options.Role);
        Append(sb, "warehouse", options.Warehouse);
        Append(sb, "db", options.Database);
        Append(sb, "schema", options.Schema);
        Append(sb, "authenticator", "snowflake_jwt");
        Append(sb, "private_key_file", resolvedPrivateKeyPath);
        if (!string.IsNullOrEmpty(options.PrivateKeyPassphrase))
            Append(sb, "private_key_pwd", options.PrivateKeyPassphrase);
        Append(sb, "application", ApplicationName);
        Append(sb, "query_tag", options.QueryTagPrefix);
        return sb.ToString();
    }

    /// <summary>Hides the passphrase so the string can be logged safely.</summary>
    public static string Redact(string connectionString) =>
        string.Join(';', connectionString.Split(';')
            .Select(part => part.StartsWith("private_key_pwd=", StringComparison.OrdinalIgnoreCase)
                ? "private_key_pwd=***"
                : part));

    // Snowflake.Data uses ';' as the separator; a literal ';' inside a value is written as ';;'.
    internal static string Escape(string value) => value.Replace(";", ";;");

    private static void Append(StringBuilder sb, string key, string value) =>
        sb.Append(key).Append('=').Append(Escape(value)).Append(';');
}
