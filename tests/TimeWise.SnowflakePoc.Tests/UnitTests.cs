using Microsoft.AspNetCore.Http;
using Polly.Timeout;
using TimeWise.SnowflakePoc.Api.Data;
using TimeWise.SnowflakePoc.Api.Identity;
using TimeWise.SnowflakePoc.Api.Options;
using TimeWise.SnowflakePoc.Api.Resilience;

namespace TimeWise.SnowflakePoc.Tests;

public sealed class ConnectionStringTests
{
    private static SnowflakeOptions Options(string? passphrase = null) => new()
    {
        Account = "ABC-XY123", User = "TIMEWISE_SVC", Role = "TIMEWISE_API_ROLE", Warehouse = "TIMEWISE_WH",
        Database = "TIMEWISE_POC", Schema = "API", PrivateKeyPath = "keys/rsa_key.p8",
        PrivateKeyPassphrase = passphrase, QueryTagPrefix = "timewise-api"
    };

    [Fact]
    public void Uses_key_pair_authentication()
    {
        var cs = SnowflakeConnectionString.Build(Options(), @"C:\keys\rsa_key.p8");
        Assert.Contains("authenticator=snowflake_jwt;", cs);
        Assert.Contains(@"private_key_file=C:\keys\rsa_key.p8;", cs);
        Assert.Contains("account=ABC-XY123;", cs);
        Assert.DoesNotContain("password", cs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Omits_passphrase_when_not_set() =>
        Assert.DoesNotContain("private_key_pwd", SnowflakeConnectionString.Build(Options(), "k.p8"));

    [Fact]
    public void Escapes_semicolons_in_values() =>
        Assert.Contains("private_key_pwd=a;;b=c;", SnowflakeConnectionString.Build(Options("a;b=c"), "k.p8"));

    [Fact]
    public void Redact_hides_passphrase()
    {
        var redacted = SnowflakeConnectionString.Redact(SnowflakeConnectionString.Build(Options("s3cret"), "k.p8"));
        Assert.DoesNotContain("s3cret", redacted);
        Assert.Contains("private_key_pwd=***", redacted);
    }

    [Fact]
    public void Relative_key_path_resolves_from_content_root() =>
        Assert.Equal(Path.GetFullPath(@"C:\repo\keys\rsa_key.p8"),
            SnowflakeConnectionFactory.ResolveKeyPath("../../keys/rsa_key.p8", @"C:\repo\src\Api"));
}

public sealed class TestHeaderUserProviderTests
{
    [Theory]
    [InlineData("Rahul.Taylor@timewise-demo.com", "rahul.taylor@timewise-demo.com")]
    [InlineData("  rahul.taylor@timewise-demo.com ", "rahul.taylor@timewise-demo.com")]
    [InlineData("", null)]
    [InlineData("not-an-email", null)]
    [InlineData("Name <a@b.com>", null)]
    public void Reads_and_normalises_header(string header, string? expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[TestHeaderUserProvider.HeaderName] = header;
        Assert.Equal(expected, new TestHeaderUserProvider().GetCurrentUserEmail(context));
    }
}

public sealed class ResilienceTests
{
    [Theory]
    [InlineData(390144, "08001", false)]  // JWT invalid: don't retry
    [InlineData(2003, "42S02", false)]    // object does not exist: don't retry
    [InlineData(270001, "08006", true)]   // connection failure: retry
    public void Classifies_snowflake_errors(int code, string sqlState, bool transient) =>
        Assert.Equal(transient, SnowflakeResilience.IsTransientSnowflakeError(code, sqlState));

    [Fact]
    public void Timeouts_are_transient() =>
        Assert.True(SnowflakeResilience.IsTransient(new TimeoutRejectedException()));

    [Fact]
    public void Login_404_is_detected_as_wrong_account() =>
        Assert.True(SnowflakeResilience.HasAccountNotFound(new Exception("Unable to connect",
            new AggregateException(new HttpRequestException("404", null, System.Net.HttpStatusCode.NotFound)))));

    [Fact]
    public void Missing_key_file_is_detected_as_key_problem() =>
        Assert.True(SnowflakeResilience.HasPrivateKeyProblem(new Exception("Unable to connect",
            new FileNotFoundException("Could not find file 'rsa_key.p8'"))));

    [Fact]
    public void Network_errors_are_not_config_problems()
    {
        var ex = new Exception("Unable to connect", new HttpRequestException("connection reset"));
        Assert.False(SnowflakeResilience.HasAccountNotFound(ex));
        Assert.False(SnowflakeResilience.HasPrivateKeyProblem(ex));
    }

    [Fact]
    public void Programming_errors_are_not_transient() =>
        Assert.False(SnowflakeResilience.IsTransient(new InvalidOperationException()));
}
