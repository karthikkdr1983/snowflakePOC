using TimeWise.SnowflakePoc.Api.Data;
using TimeWise.SnowflakePoc.Api.Models;

namespace TimeWise.SnowflakePoc.Tests;

/// <summary>In-memory stand-in for Snowflake: a manager with two reports, plus an IC with none.</summary>
public sealed class FakeEmployeeRepository : IEmployeeRepository
{
    public static readonly Employee Manager = new(6, "rahul.taylor@timewise-demo.com", "Rahul", "Taylor",
        "Engineering Manager", "Engineering", "Bengaluru", "CC-1100", 2, "Olivia Smith", "olivia.smith@timewise-demo.com",
        new DateOnly(2020, 3, 1));

    public static readonly Employee ReportA = new(21, "aarav.jones@timewise-demo.com", "Aarav", "Jones",
        "Senior Software Engineer", "Engineering", "Manchester", "CC-1100", 6, "Rahul Taylor", Manager.Email,
        new DateOnly(2023, 1, 9));

    public static readonly Employee ReportB = new(36, "rahul.smith@timewise-demo.com", "Rahul", "Smith",
        "Software Engineer", "Engineering", "London", "CC-1100", 6, "Rahul Taylor", Manager.Email,
        new DateOnly(2024, 6, 17));

    private readonly List<Employee> _employees = [Manager, ReportA, ReportB];

    public int ProfileCalls { get; private set; }
    public int TeamCalls { get; private set; }

    /// <summary>When set, every call throws this (simulates Snowflake being unreachable).</summary>
    public Exception? ThrowOnCall { get; set; }

    public Task<Employee?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        ThrowIfConfigured();
        ProfileCalls++;
        return Task.FromResult(_employees.FirstOrDefault(e => e.Email == email.ToLowerInvariant()));
    }

    public Task<IReadOnlyList<Employee>> GetDirectReportsAsync(long managerId, CancellationToken cancellationToken)
    {
        ThrowIfConfigured();
        TeamCalls++;
        return Task.FromResult<IReadOnlyList<Employee>>(_employees.Where(e => e.ManagerId == managerId).ToList());
    }

    public Task<IReadOnlyList<Employee>> GetDemoUsersAsync(int limit, CancellationToken cancellationToken)
    {
        ThrowIfConfigured();
        return Task.FromResult<IReadOnlyList<Employee>>(_employees.Take(limit).ToList());
    }

    public Task<SnowflakeSessionInfo> GetSessionInfoAsync(CancellationToken cancellationToken)
    {
        ThrowIfConfigured();
        return Task.FromResult(new SnowflakeSessionInfo("TIMEWISE_SVC", "TIMEWISE_API_ROLE", "TIMEWISE_WH",
            "TIMEWISE_POC", "API", "9.0.0"));
    }

    private void ThrowIfConfigured()
    {
        if (ThrowOnCall is not null)
            throw ThrowOnCall;
    }
}
