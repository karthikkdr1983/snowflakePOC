using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TimeWise.SnowflakePoc.Api.Data;
using TimeWise.SnowflakePoc.Api.Models;
using TimeWise.SnowflakePoc.Api.Options;

namespace TimeWise.SnowflakePoc.Api.Services;

/// <summary>A value plus whether it came from the cache (returned as the X-Cache response header).</summary>
public sealed record Cached<T>(T Value, bool FromCache);

/// <summary>
/// Adds a short-lived in-memory cache in front of Snowflake. Employee data changes rarely,
/// so this saves warehouse credits and gives fast responses for repeat page loads.
/// </summary>
public sealed class EmployeeService(
    IEmployeeRepository repository,
    IMemoryCache cache,
    IOptions<CacheOptions> cacheOptions,
    ILogger<EmployeeService> logger)
{
    private const int DemoUserLimit = 25;
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(cacheOptions.Value.TtlSeconds);

    /// <summary>The user's profile, or null if the email isn't in the view.</summary>
    public async Task<Cached<Employee?>> GetProfileAsync(string email, CancellationToken cancellationToken)
    {
        var key = $"profile:{email}";
        if (cache.TryGetValue(key, out Employee? cached))
        {
            logger.LogInformation("Cache HIT {CacheKey}", key);
            return new(cached, true);
        }

        logger.LogInformation("Cache MISS {CacheKey}", key);
        var employee = await repository.GetByEmailAsync(email, cancellationToken);
        if (employee is not null && _ttl > TimeSpan.Zero)   // unknown users aren't cached
            cache.Set(key, employee, _ttl);
        return new(employee, false);
    }

    /// <summary>The user's direct reports, or null if the user isn't in the view.</summary>
    public async Task<Cached<TeamResponse?>> GetTeamAsync(string email, CancellationToken cancellationToken)
    {
        var profile = await GetProfileAsync(email, cancellationToken);
        if (profile.Value is null)
            return new(null, profile.FromCache);

        var manager = profile.Value;
        var key = $"team:{manager.EmployeeId}";
        if (cache.TryGetValue(key, out IReadOnlyList<Employee>? reports) && reports is not null)
        {
            logger.LogInformation("Cache HIT {CacheKey}", key);
            return new(new TeamResponse(manager, reports), profile.FromCache);
        }

        logger.LogInformation("Cache MISS {CacheKey}", key);
        reports = await repository.GetDirectReportsAsync(manager.EmployeeId, cancellationToken);
        if (_ttl > TimeSpan.Zero)
            cache.Set(key, reports, _ttl);
        return new(new TeamResponse(manager, reports), false);
    }

    /// <summary>Users for the demo page's dropdown (leaders first, then a few ICs).</summary>
    public async Task<IReadOnlyList<Employee>> GetDemoUsersAsync(CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync("demo-users", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _ttl > TimeSpan.Zero ? _ttl : TimeSpan.FromSeconds(1);
            return repository.GetDemoUsersAsync(DemoUserLimit, cancellationToken);
        }) ?? [];
}
