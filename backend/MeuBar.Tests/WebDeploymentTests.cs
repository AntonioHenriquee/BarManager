using System.Net;
using System.Text.RegularExpressions;
using MeuBar;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace MeuBar.Tests;

public class DatabaseSettingsTests
{
    static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(x => x.Key, x => (string?)x.Value)).Build();

    [Fact]
    public void ManagedUrlPreservesEscapedCredentialsAndTlsVerification()
    {
        var config = Config(("DATABASE_URL", "postgresql://bar%40test:p%40ss%3Bword%3D1@db.example:5433/meu%20bar?sslmode=verify-full&application_name=meubar"));
        var result = new NpgsqlConnectionStringBuilder(DatabaseSettings.ConnectionString(config));
        Assert.Equal("bar@test", result.Username);
        Assert.Equal("p@ss;word=1", result.Password);
        Assert.Equal("meu bar", result.Database);
        Assert.Equal("db.example", result.Host);
        Assert.Equal(5433, result.Port);
        Assert.Equal(SslMode.VerifyFull, result.SslMode);
        Assert.Equal("meubar", result.ApplicationName);
    }

    [Fact]
    public void ExistingNpgsqlConfigurationHasPriority()
    {
        var original = "Host=localhost;Database=local;Username=local;Password=test-only";
        Assert.Equal(original, DatabaseSettings.ConnectionString(Config(("ConnectionStrings:Database", original), ("DATABASE_URL", "invalid"))));
    }

    [Theory]
    [InlineData("https://user:secret-not-for-logs@db.example/bar")]
    [InlineData("postgres://user:secret-not-for-logs@db.example/")]
    [InlineData("postgres://user:secret-not-for-logs@db.example/bar?unsupported=value")]
    [InlineData("postgres://user:secret-not-for-logs@db.example/bar?sslmode=invalid")]
    public void InvalidUrlsAreRejectedWithoutExposingCredentials(string url)
    {
        var error = Assert.Throws<InvalidOperationException>(() => DatabaseSettings.ConnectionString(Config(("DATABASE_URL", url))));
        Assert.DoesNotContain("secret-not-for-logs", error.ToString());
    }
}

public class WebDeploymentTests(SystemFixture fixture) : IClassFixture<SystemFixture>
{
    [Fact]
    public async Task SameOriginServesActualAngularBundleAndKeepsApiAuthentication()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MeuBar.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var webRoot = Path.Combine(root!.FullName, "frontend", "dist", "meu-bar", "browser");
        Assert.True(File.Exists(Path.Combine(webRoot, "index.html")), "Execute npm --prefix frontend run build antes dos testes de publicação web.");
        await using var server = fixture.Factory.WithWebHostBuilder(b =>
        {
            b.UseWebRoot(webRoot);
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Security:SecureCookies"] = "true" }));
        });
        using var client = server.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        var index = await response.Content.ReadAsStringAsync();
        Assert.Contains("<app-root>", index);
        Assert.DoesNotContain("onload=", index, StringComparison.OrdinalIgnoreCase);
        var stylesheet = Regex.Match(index, "<link[^>]*rel=\"stylesheet\"[^>]*href=\"([^\"]+)\"");
        Assert.True(stylesheet.Success);
        var css = await client.GetAsync("/" + stylesheet.Groups[1].Value.TrimStart('/'));
        Assert.Equal(HttpStatusCode.OK, css.StatusCode);
        Assert.Equal("text/css", css.Content.Headers.ContentType!.MediaType);
        var script = Regex.Match(index, "<script[^>]*src=\"([^\"]+)\"");
        Assert.True(script.Success);
        var javascript = await client.GetAsync("/" + script.Groups[1].Value.TrimStart('/'));
        Assert.Equal(HttpStatusCode.OK, javascript.StatusCode);
        Assert.True((await javascript.Content.ReadAsByteArrayAsync()).Length > 1000);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/products")).StatusCode);
        var missing = await client.GetAsync("/api/inexistente");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("application/json", missing.Content.Headers.ContentType!.MediaType);
        Assert.Contains("<app-root>", await client.GetStringAsync("/mesas"));
        var login = await SystemFixture.Send(client, "/auth/login", new { login = "admin", password = fixture.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), value => value.Contains("secure", StringComparison.OrdinalIgnoreCase) && value.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/products")).StatusCode);
    }
}
