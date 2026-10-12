using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Polly.Timeout;
using TimeWise.SnowflakePoc.Api.Data;
using TimeWise.SnowflakePoc.Api.Identity;

namespace TimeWise.SnowflakePoc.Tests;

public sealed class ApiTests : IDisposable
{
    private readonly FakeEmployeeRepository _repository = new();
    private readonly WebApplicationFactory<Program> _factory;

    public ApiTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Snowflake:Account", "TESTORG-TESTACCOUNT");
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IEmployeeRepository>(_repository));
        });
    }

    public void Dispose() => _factory.Dispose();

    private HttpClient ClientFor(string? email)
    {
        var client = _factory.CreateClient();
        if (email is not null)
            client.DefaultRequestHeaders.Add(TestHeaderUserProvider.HeaderName, email);
        return client;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-an-email")]
    public async Task Profile_without_valid_user_header_returns_401(string? header)
    {
        var response = await ClientFor(header).GetAsync("/api/me/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Profile_for_unknown_user_returns_404()
    {
        var response = await ClientFor("nobody@timewise-demo.com").GetAsync("/api/me/profile");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Profile_returns_employee_and_second_call_is_served_from_cache()
    {
        var client = ClientFor("Rahul.Taylor@timewise-demo.com");   // case-insensitive

        var first = await client.GetAsync("/api/me/profile");
        var second = await client.GetAsync("/api/me/profile");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("MISS", first.Headers.GetValues("X-Cache").Single());
        Assert.Equal("HIT", second.Headers.GetValues("X-Cache").Single());
        Assert.Equal(1, _repository.ProfileCalls);

        var body = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(6, body.GetProperty("employeeId").GetInt64());
        Assert.Equal("Rahul Taylor", body.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Team_returns_direct_reports()
    {
        var response = await ClientFor(FakeEmployeeRepository.Manager.Email).GetAsync("/api/me/team");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("count").GetInt32());
        Assert.Equal(6, body.GetProperty("manager").GetProperty("employeeId").GetInt64());
    }

    [Fact]
    public async Task Team_for_individual_contributor_is_empty()
    {
        var response = await ClientFor(FakeEmployeeRepository.ReportA.Email).GetAsync("/api/me/team");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Snowflake_timeout_returns_503_problem_details()
    {
        _repository.ThrowOnCall = new TimeoutRejectedException();

        var response = await ClientFor(FakeEmployeeRepository.Manager.Email).GetAsync("/api/me/profile");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Health_returns_session_context()
    {
        var response = await ClientFor(null).GetAsync("/health/snowflake");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("TIMEWISE_SVC", body.GetProperty("session").GetProperty("currentUser").GetString());
    }

    [Fact]
    public async Task Demo_page_is_served()
    {
        var response = await ClientFor(null).GetAsync("/demo.html");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
