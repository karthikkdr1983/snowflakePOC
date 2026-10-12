using Polly;
using Polly.Retry;
using Polly.Timeout;
using Snowflake.Data.Client;
using TimeWise.SnowflakePoc.Api.Options;

namespace TimeWise.SnowflakePoc.Api.Resilience;

/// <summary>
/// Retry plus a per-attempt timeout around Snowflake calls. Only transient failures are retried;
/// authentication, permission and SQL errors fail fast.
/// </summary>
public static class SnowflakeResilience
{
    public const string PipelineKey = "snowflake";

    public static ResiliencePipeline Create(ResilienceOptions options, ILogger logger)
    {
        var builder = new ResiliencePipelineBuilder();
        if (options.MaxRetries > 0)
        {
            builder.AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(IsTransient),
                MaxRetryAttempts = options.MaxRetries,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(500),
                OnRetry = args =>
                {
                    logger.LogWarning(args.Outcome.Exception,
                        "Snowflake call failed (attempt {Attempt}); retrying in {Delay}",
                        args.AttemptNumber + 1, args.RetryDelay);
                    return default;
                }
            });
        }

        return builder
            .AddTimeout(TimeSpan.FromSeconds(options.AttemptTimeoutSeconds))
            .Build();
    }

    public static bool IsTransient(Exception exception) => exception switch
    {
        TimeoutRejectedException => true,
        HttpRequestException => true,
        IOException => true,
        SnowflakeDbException sf => IsTransientSnowflakeError(sf.ErrorCode, sf.SqlState)
                                   && InnerChain(sf).OfType<SnowflakeDbException>()
                                       .All(inner => IsTransientSnowflakeError(inner.ErrorCode, inner.SqlState))
                                   && !HasAccountNotFound(sf) && !HasPrivateKeyProblem(sf),
        _ => false
    };

    /// <summary>
    /// The real Snowflake error code. Login failures arrive as a generic 270001 wrapping the server's code (e.g. 390144).
    /// </summary>
    public static int? RootSnowflakeErrorCode(Exception exception) =>
        InnerChain(exception).OfType<SnowflakeDbException>().LastOrDefault()?.ErrorCode
        ?? (exception as SnowflakeDbException)?.ErrorCode;

    // The driver wraps login failures in a generic "Unable to connect" (270001); look inside for config errors
    // that won't fix themselves. HTTP 404/403 from login = wrong account identifier.
    internal static bool HasAccountNotFound(Exception exception) =>
        InnerChain(exception).Any(e => e is HttpRequestException
        {
            StatusCode: System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden
        });

    internal static bool HasPrivateKeyProblem(Exception exception) =>
        InnerChain(exception).Any(e => e is FileNotFoundException or DirectoryNotFoundException
                                       || e.Message.Contains("private key", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<Exception> InnerChain(Exception exception)
    {
        for (var e = exception.InnerException; e is not null; e = e.InnerException)
            yield return e;
    }

    // 390xxx = login errors (bad key, unknown user, locked account): retrying won't help.
    // SQLSTATE 42 = syntax/object/privilege, 22 = data, 28 = authorization, 0A = not supported.
    internal static bool IsTransientSnowflakeError(int errorCode, string? sqlState)
    {
        if (errorCode is >= 390000 and < 391000)
            return false;
        if (sqlState is not null && (sqlState.StartsWith("42") || sqlState.StartsWith("22")
                                     || sqlState.StartsWith("28") || sqlState.StartsWith("0A")))
            return false;
        return true;
    }
}
