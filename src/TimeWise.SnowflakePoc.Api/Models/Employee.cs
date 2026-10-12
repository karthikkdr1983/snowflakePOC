namespace TimeWise.SnowflakePoc.Api.Models;

/// <summary>One row of TIMEWISE_POC.API.V_EMPLOYEE.</summary>
public sealed record Employee(
    long EmployeeId,
    string Email,
    string FirstName,
    string LastName,
    string? JobTitle,
    string? Department,
    string? Location,
    string? CostCentre,
    long? ManagerId,
    string? ManagerName,
    string? ManagerEmail,
    DateOnly? HireDate)
{
    public string DisplayName => $"{FirstName} {LastName}";
}

/// <summary>The current user's direct reports.</summary>
public sealed record TeamResponse(Employee Manager, IReadOnlyList<Employee> DirectReports)
{
    public int Count => DirectReports.Count;
}

public sealed record SnowflakeSessionInfo(
    string? CurrentUser,
    string? CurrentRole,
    string? CurrentWarehouse,
    string? CurrentDatabase,
    string? CurrentSchema,
    string? SnowflakeVersion);
