using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Claims.Api.Controllers;
using Claims.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;

namespace Claims.Tests;

// One Postgres container and one API host for the class; each test makes its own claim, so order doesn't matter.
public sealed class ApiFixture : IAsyncLifetime
{
    readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17").Build();
    public WebApplicationFactory<Program> App { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        App = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Claims", _db.GetConnectionString())
            .UseSetting("Jwt:Key", Key)
            .UseSetting("DevLogin:Enabled", "true"));
    }

    public async Task DisposeAsync()
    {
        await App.DisposeAsync();
        await _db.DisposeAsync();
    }

    const string Key = "test-signing-key-at-least-32-bytes-long!!";

    // A validly signed token with any role, as a future identity provider might issue.
    public HttpClient WithRole(string role)
    {
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "claims-api",
            Audience = "claims-web",
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["sub"] = Guid.NewGuid().ToString(), ["role"] = role },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256),
        });
        var client = App.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<HttpClient> LoginAs(Guid userId)
    {
        var client = App.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/dev-login", new { userId });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<DevLoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<Guid> SubmitClaim(HttpClient claimant)
    {
        var res = await claimant.PostAsJsonAsync("/api/claims",
            new { type = "Motor", market = "AU", policyNumber = "AU-MOT-9999", description = "Test claim" });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<ClaimDetail>(Json))!.Claim.Id;
    }

    // Matches the API: enums travel as names.
    public static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };
}

public class SecurityTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Claimant_gets_404_for_another_claimants_claim()
    {
        var aisha = await api.LoginAs(Seed.Aisha);
        var ben = await api.LoginAs(Seed.Ben);
        var id = await api.SubmitClaim(aisha);

        Assert.Equal(HttpStatusCode.OK, (await aisha.GetAsync($"/api/claims/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ben.GetAsync($"/api/claims/{id}")).StatusCode);
    }

    [Fact]
    public async Task Claimant_cannot_settle_403()
    {
        var aisha = await api.LoginAs(Seed.Aisha);
        var id = await api.SubmitClaim(aisha);

        var res = await aisha.PostAsJsonAsync($"/api/claims/{id}/settle", new { amount = 100m });

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Double_pick_up_one_wins_other_gets_409()
    {
        var id = await api.SubmitClaim(await api.LoginAs(Seed.Aisha));
        var chen = await api.LoginAs(Seed.Chen);
        var devi = await api.LoginAs(Seed.Devi);

        // At the same time: the loser fails either the Version check or Assign's Submitted rule; both are 409.
        var results = await Task.WhenAll(
            chen.PostAsync($"/api/claims/{id}/assign", null),
            devi.PostAsync($"/api/claims/{id}/assign", null));

        Assert.Equal(
            new[] { HttpStatusCode.NoContent, HttpStatusCode.Conflict },
            results.Select(r => r.StatusCode).Order());
    }

    [Fact]
    public async Task No_token_gets_401()
    {
        var res = await api.App.CreateClient().GetAsync("/api/claims");

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Unknown_role_sees_no_claims()
    {
        var id = await api.SubmitClaim(await api.LoginAs(Seed.Aisha));
        var auditor = api.WithRole("Auditor");

        Assert.Equal("[]", await auditor.GetStringAsync("/api/claims"));
        Assert.Equal(HttpStatusCode.NotFound, (await auditor.GetAsync($"/api/claims/{id}")).StatusCode);
    }

    [Theory]
    [InlineData("Development", "false")] // switch off
    [InlineData("Production", "true")]   // switch on, wrong environment
    public async Task Dev_login_needs_Development_and_the_switch(string environment, string enabled)
    {
        await using var app = api.App.WithWebHostBuilder(b => b
            .UseEnvironment(environment)
            .UseSetting("DevLogin:Enabled", enabled));

        var res = await app.CreateClient().PostAsJsonAsync("/api/auth/dev-login", new { userId = Seed.Farah });

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
