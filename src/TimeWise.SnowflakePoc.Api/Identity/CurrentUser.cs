using System.Net.Mail;

namespace TimeWise.SnowflakePoc.Api.Identity;

/// <summary>
/// Works out who the signed-in user is. Phase 1 reads a test header;
/// Phase 2 swaps in an Entra ID implementation (the email/UPN claim) without changing the endpoints.
/// </summary>
public interface ICurrentUserProvider
{
    /// <summary>The user's email in lower case, or null if no user is identified.</summary>
    string? GetCurrentUserEmail(HttpContext httpContext);
}

/// <summary>
/// POC only: trusts the X-Test-User-Email header. Never enable this outside development/demo.
/// </summary>
public sealed class TestHeaderUserProvider : ICurrentUserProvider
{
    public const string HeaderName = "X-Test-User-Email";

    public string? GetCurrentUserEmail(HttpContext httpContext)
    {
        var value = httpContext.Request.Headers[HeaderName].ToString().Trim();
        if (value.Length == 0 || value.Length > 256)
            return null;
        if (!MailAddress.TryCreate(value, out var address) || address.Address != value)
            return null;
        return value.ToLowerInvariant();
    }
}
