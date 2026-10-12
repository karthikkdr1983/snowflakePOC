using System.Data.Common;
using Microsoft.Extensions.Options;
using Snowflake.Data.Client;
using TimeWise.SnowflakePoc.Api.Options;

namespace TimeWise.SnowflakePoc.Api.Data;

public interface ISnowflakeConnectionFactory
{
    /// <summary>Opens a pooled connection as the service account.</summary>
    Task<DbConnection> OpenAsync(CancellationToken cancellationToken);
}

public sealed class SnowflakeConnectionFactory : ISnowflakeConnectionFactory
{
    private readonly string _connectionString;

    public SnowflakeConnectionFactory(
        IOptions<SnowflakeOptions> options,
        IHostEnvironment environment,
        ILogger<SnowflakeConnectionFactory> logger)
    {
        var o = options.Value;
        var keyPath = ResolveKeyPath(o.PrivateKeyPath, environment.ContentRootPath);
        if (!File.Exists(keyPath))
            logger.LogWarning("Snowflake private key not found at {KeyPath}. Connections will fail until it exists.", keyPath);

        _connectionString = SnowflakeConnectionString.Build(o, keyPath);
        logger.LogInformation("Snowflake connection: {ConnectionString}", SnowflakeConnectionString.Redact(_connectionString));
    }

    public async Task<DbConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SnowflakeDbConnection { ConnectionString = _connectionString };
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public static string ResolveKeyPath(string path, string contentRoot) =>
        Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(contentRoot, path));
}
