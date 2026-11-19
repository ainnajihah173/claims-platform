using System.Text;
using System.Text.Json.Serialization;
using Claims.Api.Controllers;
using Claims.Api.Data;
using Claims.Api.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// The OpenAPI generator reads these options, not the MVC ones; without it Scalar shows enums as integers.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Claims")));
// Set in appsettings.json so every environment publishes by default. Only the API tests blank it:
// they run without Kafka and must not write to the dev topic.
if (!string.IsNullOrEmpty(builder.Configuration["Kafka:BootstrapServers"]))
{
    builder.Services.AddHostedService<OutboxPublisher>();
    builder.Services.AddHostedService<ProjectionConsumer>();
}

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Set Jwt:Key (user-secrets or the Jwt__Key env var), at least 32 characters.");
if (jwtKey.Length < 32) throw new InvalidOperationException("Jwt:Key must be at least 32 characters (HMAC-SHA256 needs 256 bits).");
builder.Services.AddAuthentication().AddJwtBearer(o =>
{
    o.MapInboundClaims = false; // keep "sub" / "role" as issued
    o.TokenValidationParameters = new()
    {
        ValidateIssuer = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidateAudience = true,
        ValidAudience = builder.Configuration["Jwt:Audience"],
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        NameClaimType = "name",
        RoleClaimType = "role",
    };
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("ClaimantOnly", p => p.RequireRole("Claimant"))
    .AddPolicy("StaffOnly", p => p.RequireRole("Officer", "Manager"))
    // Deny by default: every endpoint needs a valid token unless it says [AllowAnonymous].
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated(); // shortcut: see Seed.cs
    Seed.Run(db, DateTimeOffset.UtcNow);
}

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
