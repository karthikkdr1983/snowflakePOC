using System.Diagnostics;
using Microsoft.Extensions.Options;
using Polly;
using TimeWise.SnowflakePoc.Api.Data;
using TimeWise.SnowflakePoc.Api.Identity;
using TimeWise.SnowflakePoc.Api.Infrastructure;
using TimeWise.SnowflakePoc.Api.Options;
using TimeWise.SnowflakePoc.Api.Resilience;
using TimeWise.SnowflakePoc.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// POC: read user-secrets (account, key passphrase) however the app is started, not only in Development.
// In Azure, App Service settings / Key Vault take over and this is a no-op.
builder.Configuration.AddUserSecrets<Program>(optional: true);

// ---- Configuration (validated at startup so a bad config fails fast with a clear message) ----
builder.Services.AddOptions<SnowflakeOptions>().BindConfiguration(SnowflakeOptions.SectionName)
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<AuthOptions>().BindConfiguration(AuthOptions.SectionName)
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<CacheOptions>().BindConfiguration(CacheOptions.SectionName)
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<ResilienceOptions>().BindConfiguration(ResilienceOptions.SectionName)
    .ValidateDataAnnotations().ValidateOnStart();

// ---- Snowflake access ----
builder.Services.AddSingleton<ISnowflakeConnectionFactory, SnowflakeConnectionFactory>();
builder.Services.AddKeyedSingleton<ResiliencePipeline>(SnowflakeResilience.PipelineKey, (sp, _) =>
    SnowflakeResilience.Create(
        sp.GetRequiredService<IOptions<ResilienceOptions>>().Value,
        sp.GetRequiredService<ILoggerFactory>().CreateLogger("TimeWise.SnowflakePoc.Api.Resilience")));
builder.Services.AddSingleton<IEmployeeRepository, EmployeeRepository>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<EmployeeService>();

// ---- Who is the user? Phase 1: test header. Phase 2: Entra ID (Microsoft.Identity.Web). ----
var authMode = builder.Configuration[$"{AuthOptions.SectionName}:Mode"] ?? AuthOptions.TestHeaderMode;
var isTestHeaderMode = string.Equals(authMode, AuthOptions.TestHeaderMode, StringComparison.OrdinalIgnoreCase);
if (!isTestHeaderMode)
    throw new InvalidOperationException($"Auth mode '{authMode}' is not implemented yet. Use '{AuthOptions.TestHeaderMode}' for Phase 1.");
builder.Services.AddSingleton<ICurrentUserProvider, TestHeaderUserProvider>();

// ---- Errors, docs ----
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<SnowflakeExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "TimeWise Snowflake POC", Version = "v1" });
    c.OperationFilter<TestUserHeaderOperationFilter>();
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSwagger();
app.UseSwaggerUI();
app.UseStaticFiles();

// ---- Endpoints ----
app.MapGet("/", () => Results.Redirect("/demo.html")).ExcludeFromDescription();

var me = app.MapGroup("/api/me").WithTags("Me");

me.MapGet("/profile", async (HttpContext http, ICurrentUserProvider currentUser, EmployeeService employees, CancellationToken ct) =>
{
    var email = currentUser.GetCurrentUserEmail(http);
    if (email is null)
        return MissingUser();

    var result = await employees.GetProfileAsync(email, ct);
    SetCacheHeader(http, result.FromCache);
    return result.Value is null
        ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Employee not found",
            detail: $"No active employee with email '{email}' in the Snowflake directory.")
        : Results.Ok(result.Value);
})
.WithName("GetMyProfile")
.WithSummary("The signed-in user's employee record from Snowflake.");

me.MapGet("/team", async (HttpContext http, ICurrentUserProvider currentUser, EmployeeService employees, CancellationToken ct) =>
{
    var email = currentUser.GetCurrentUserEmail(http);
    if (email is null)
        return MissingUser();

    var result = await employees.GetTeamAsync(email, ct);
    SetCacheHeader(http, result.FromCache);
    return result.Value is null
        ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Employee not found",
            detail: $"No active employee with email '{email}' in the Snowflake directory.")
        : Results.Ok(result.Value);
})
.WithName("GetMyTeam")
.WithSummary("The signed-in user's direct reports (used for timesheet approval).");

app.MapGet("/health/snowflake", async (IEmployeeRepository repository, CancellationToken ct) =>
{
    var stopwatch = Stopwatch.StartNew();
    var session = await repository.GetSessionInfoAsync(ct);
    return Results.Ok(new { status = "ok", elapsedMs = stopwatch.ElapsedMilliseconds, session });
})
.WithTags("Health")
.WithSummary("Connects as the service account and returns the Snowflake session context.");

// Demo helper: users for the demo page's dropdown. Remove when Entra ID replaces the test header.
app.MapGet("/api/demo/users", async (EmployeeService employees, CancellationToken ct) =>
    Results.Ok(await employees.GetDemoUsersAsync(ct)))
.WithTags("Demo")
.WithSummary("POC only: sample users to pick from on the demo page.");

app.Run();

static IResult MissingUser() => Results.Problem(
    statusCode: StatusCodes.Status401Unauthorized,
    title: "No user identified",
    detail: $"Send the '{TestHeaderUserProvider.HeaderName}' header with a valid email (Phase 1 test mode).");

static void SetCacheHeader(HttpContext http, bool fromCache) =>
    http.Response.Headers["X-Cache"] = fromCache ? "HIT" : "MISS";

public partial class Program;
