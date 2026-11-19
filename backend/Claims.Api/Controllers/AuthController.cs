using System.ComponentModel.DataAnnotations;
using System.Text;
using Claims.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Claims.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, IConfiguration config, IWebHostEnvironment env) : ControllerBase
{
    // shortcut: pick a seeded user, no password; replace with OIDC (Entra ID / Keycloak) before any shared environment.
    [HttpPost("dev-login"), AllowAnonymous]
    public async Task<ActionResult<DevLoginResponse>> DevLogin(DevLoginRequest r)
    {
        // Two switches: a deployment wrongly running as Development still needs DevLogin:Enabled, which only launchSettings.json sets.
        if (!env.IsDevelopment() || !config.GetValue<bool>("DevLogin:Enabled")) return NotFound();
        var user = await db.Users.FindAsync(r.UserId);
        if (user is null) return Unauthorized();

        var claims = new Dictionary<string, object>
        {
            ["sub"] = user.Id.ToString(),
            ["role"] = user.Role.ToString(),
            ["name"] = user.Name,
        };
        if (user.Team is not null) claims["team"] = user.Team;

        var expires = DateTime.UtcNow.AddHours(1);
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = config["Jwt:Issuer"],
            Audience = config["Jwt:Audience"],
            Expires = expires,
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)), SecurityAlgorithms.HmacSha256),
        });
        return new DevLoginResponse(token, expires);
    }
}

public record DevLoginRequest([Required] Guid? UserId);
public record DevLoginResponse(string Token, DateTime ExpiresAt);
