using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using Polly;
using Snowflake.Data.Client;
using TimeWise.SnowflakePoc.Api.Models;
using TimeWise.SnowflakePoc.Api.Options;
using TimeWise.SnowflakePoc.Api.Resilience;

namespace TimeWise.SnowflakePoc.Api.Data;

public interface IEmployeeRepository
{
    Task<Employee?> GetByEmailAsync(string email, CancellationToken cancellationToken);
    Task<IReadOnlyList<Employee>> GetDirectReportsAsync(long managerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Employee>> GetDemoUsersAsync(int limit, CancellationToken cancellationToken);
    Task<SnowflakeSessionInfo> GetSessionInfoAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reads employee data from the secure view. User input is always bound as a parameter, never concatenated,
/// and every statement carries a QUERY_TAG so it can be traced in Snowflake query history.
/// </summary>
public sealed class EmployeeRepository(
    ISnowflakeConnectionFactory connectionFactory,
    [FromKeyedServices(SnowflakeResilience.PipelineKey)] ResiliencePipeline pipeline,
    IOptions<SnowflakeOptions> options,
    ILogger<EmployeeRepository> logger) : IEmployeeRepository
{
    // Resolved against the database and schema in the connection (Snowflake:Database / Snowflake:Schema).
    private const string View = "V_EMPLOYEE";

    private const string Columns =
        "EMPLOYEE_ID, EMAIL, FIRST_NAME, LAST_NAME, JOB_TITLE, DEPARTMENT, LOCATION, " +
        "COST_CENTRE, MANAGER_ID, MANAGER_NAME, MANAGER_EMAIL, HIRE_DATE";

    private readonly SnowflakeOptions _options = options.Value;

    public async Task<Employee?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var rows = await QueryEmployeesAsync(
            "get-profile",
            $"SELECT {Columns} FROM {View} WHERE EMAIL = LOWER(?) LIMIT 1",
            cmd => AddParameter(cmd, "1", email, DbType.String),
            cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public Task<IReadOnlyList<Employee>> GetDirectReportsAsync(long managerId, CancellationToken cancellationToken) =>
        QueryEmployeesAsync(
            "get-team",
            $"SELECT {Columns} FROM {View} WHERE MANAGER_ID = ? ORDER BY LAST_NAME, FIRST_NAME",
            cmd => AddParameter(cmd, "1", managerId, DbType.Int64),
            cancellationToken);

    public Task<IReadOnlyList<Employee>> GetDemoUsersAsync(int limit, CancellationToken cancellationToken) =>
        QueryEmployeesAsync(
            "demo-users",
            $"SELECT {Columns} FROM {View} ORDER BY EMPLOYEE_ID LIMIT ?",
            cmd => AddParameter(cmd, "1", limit, DbType.Int32),
            cancellationToken);

    public Task<SnowflakeSessionInfo> GetSessionInfoAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(
            "health",
            "SELECT CURRENT_USER(), CURRENT_ROLE(), CURRENT_WAREHOUSE(), CURRENT_DATABASE(), CURRENT_SCHEMA(), CURRENT_VERSION()",
            _ => { },
            async (reader, ct) =>
            {
                await reader.ReadAsync(ct);
                return new SnowflakeSessionInfo(
                    GetString(reader, 0), GetString(reader, 1), GetString(reader, 2),
                    GetString(reader, 3), GetString(reader, 4), GetString(reader, 5));
            },
            cancellationToken);

    private Task<IReadOnlyList<Employee>> QueryEmployeesAsync(
        string operation, string sql, Action<DbCommand> bind, CancellationToken cancellationToken) =>
        ExecuteAsync<IReadOnlyList<Employee>>(operation, sql, bind, async (reader, ct) =>
        {
            var list = new List<Employee>();
            while (await reader.ReadAsync(ct))
                list.Add(Map(reader));
            return list;
        }, cancellationToken);

    private async Task<T> ExecuteAsync<T>(
        string operation,
        string sql,
        Action<DbCommand> bind,
        Func<DbDataReader, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken)
    {
        var queryTag = $"{_options.QueryTagPrefix}/{operation}";
        var stopwatch = Stopwatch.StartNew();

        var result = await pipeline.ExecuteAsync(async ct =>
        {
            await using var connection = await connectionFactory.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = _options.CommandTimeoutSeconds;
            if (command is SnowflakeDbCommand snowflakeCommand)
                snowflakeCommand.QueryTag = queryTag;
            bind(command);

            await using var reader = await command.ExecuteReaderAsync(ct);
            return await read(reader, ct);
        }, cancellationToken);

        logger.LogInformation("Snowflake query {QueryTag} completed in {ElapsedMs} ms", queryTag, stopwatch.ElapsedMilliseconds);
        return result;
    }

    private static void AddParameter(DbCommand command, string name, object value, DbType type)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name; // Snowflake.Data binds '?' placeholders by position: "1", "2", ...
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static Employee Map(DbDataReader r) => new(
        EmployeeId: Convert.ToInt64(r["EMPLOYEE_ID"]),
        Email: (string)r["EMAIL"],
        FirstName: (string)r["FIRST_NAME"],
        LastName: (string)r["LAST_NAME"],
        JobTitle: r["JOB_TITLE"] as string,
        Department: r["DEPARTMENT"] as string,
        Location: r["LOCATION"] as string,
        CostCentre: r["COST_CENTRE"] as string,
        ManagerId: r["MANAGER_ID"] is DBNull ? null : Convert.ToInt64(r["MANAGER_ID"]),
        ManagerName: r["MANAGER_NAME"] as string,
        ManagerEmail: r["MANAGER_EMAIL"] as string,
        HireDate: r["HIRE_DATE"] is DBNull ? null : DateOnly.FromDateTime(Convert.ToDateTime(r["HIRE_DATE"])));

    private static string? GetString(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal).ToString();
}
