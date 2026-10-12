using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Polly.Timeout;
using Snowflake.Data.Client;
using TimeWise.SnowflakePoc.Api.Resilience;

namespace TimeWise.SnowflakePoc.Api.Infrastructure;

/// <summary>
/// Turns Snowflake failures into 503 ProblemDetails. In Development the driver message and a troubleshooting
/// hint are included; neither ever contains the private key or passphrase.
/// </summary>
public sealed class SnowflakeExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<SnowflakeExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (title, errorCode) = exception switch
        {
            SnowflakeDbException sf => ("Snowflake request failed", SnowflakeResilience.RootSnowflakeErrorCode(sf)),
            TimeoutRejectedException => ("Snowflake did not respond in time", null),
            HttpRequestException => ("Snowflake could not be reached", null),
            _ => (null, null)
        };
        if (title is null)
            return false;

        logger.LogError(exception, "{Title}", title);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = title,
            Detail = environment.IsDevelopment()
                ? $"{exception.Message} | {FirstLine(Innermost(exception).Message)}"
                : "The employee directory is temporarily unavailable. Please try again shortly."
        };
        if (errorCode is not null)
            problem.Extensions["snowflakeErrorCode"] = errorCode;
        if (environment.IsDevelopment() && Hint(exception) is { } hint)
            problem.Extensions["hint"] = hint;

        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    /// <summary>Plain-English pointers for the errors people usually hit when setting up.</summary>
    internal static string? Hint(Exception exception)
    {
        if (SnowflakeResilience.HasAccountNotFound(exception))
            return "Snowflake returned 404: check Snowflake:Account. Use ORGNAME-ACCOUNTNAME (01_setup.sql prints it).";
        if (SnowflakeResilience.HasPrivateKeyProblem(exception))
            return "Private key problem: check Snowflake:PrivateKeyPath and Snowflake:PrivateKeyPassphrase.";
        var code = SnowflakeResilience.RootSnowflakeErrorCode(exception);
        if (code == 390144)
            return "JWT rejected: the public key on TIMEWISE_SVC is missing or doesn't match keys\\rsa_key.p8. " +
                   "Run the ALTER USER statement printed by New-SnowflakeKeyPair.ps1, then compare RSA_PUBLIC_KEY_FP in DESC USER.";
        if (code == 390100)
            return "User not found or key not set: check Snowflake:User and that 03_set_public_key.sql was run.";
        if (exception.ToString().Contains("No such host", StringComparison.OrdinalIgnoreCase))
            return "DNS lookup failed: check Snowflake:Account and your network/proxy.";
        return null;
    }

    private static Exception Innermost(Exception exception)
    {
        while (exception.InnerException is not null)
            exception = exception.InnerException;
        return exception;
    }

    private static string FirstLine(string message) =>
        message.Split('\n', 2)[0].Split(": Org.BouncyCastle", 2)[0].TrimEnd('\r');
}
