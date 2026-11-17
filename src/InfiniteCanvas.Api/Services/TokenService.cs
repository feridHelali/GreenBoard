using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using InfiniteCanvas.Api.Data;
using InfiniteCanvas.Api.Models;

namespace InfiniteCanvas.Api.Services;

public sealed class TokenService : ITokenService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    public TokenService(AppDbContext db, IConfiguration config, IWebHostEnvironment env)
    {
        _db = db;
        _config = config;
        _env = env;
    }

    public string CreateAccessToken(Models.AppUser user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt__Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Email, user.Email!),
            new Claim(ClaimTypes.Name, user.DisplayName ?? user.Email!),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt__Issuer"] ?? "https://api.greenboard.local",
            audience: _config["Jwt__Audience"] ?? "greenboard-app",
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string CreateRefreshToken(Models.AppUser user)
    {
        var randomBytes = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    public async Task SaveRefreshTokenAsync(string userId, string refreshToken)
    {
        var existing = _db.RefreshTokens.FirstOrDefault(rt => rt.UserId == userId && rt.Revoked == null);
        if (existing != null)
        {
            existing.Revoked = DateTimeOffset.UtcNow;
            existing.ReplacedByToken = refreshToken;
        }

        _db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            Token = refreshToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
            CreatedAt = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync();
    }

    public async Task<bool> ValidateRefreshTokenAsync(string userId, string refreshToken)
    {
        var token = _db.RefreshTokens.FirstOrDefault(rt =>
            rt.UserId == userId &&
            rt.Token == refreshToken &&
            rt.Revoked == null &&
            rt.ExpiresAt.HasValue && rt.ExpiresAt.Value > DateTimeOffset.UtcNow);

        return token != null;
    }

    public async Task RevokeRefreshTokenAsync(string userId, string refreshToken)
    {
        var token = _db.RefreshTokens.FirstOrDefault(rt =>
            rt.UserId == userId && rt.Token == refreshToken && rt.Revoked == null);
        if (token != null)
        {
            token.Revoked = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    public async Task RevokeAllRefreshTokensAsync(string userId)
    {
        var tokens = _db.RefreshTokens.Where(rt => rt.UserId == userId && rt.Revoked == null).ToList();
        foreach (var t in tokens)
        {
            t.Revoked = DateTimeOffset.UtcNow;
        }
        if (tokens.Count > 0)
        {
            await _db.SaveChangesAsync();
        }
    }

    public System.Security.Claims.ClaimsPrincipal? GetPrincipalFromExpiredToken(string? accessToken)
    {
        if (string.IsNullOrEmpty(accessToken))
        {
            return null;
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt__Key"]!));
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = false, // Token expir\u00e9 accept\u00e9 pour extraire le principal
            ValidateIssuerSigningKey = true,
            ValidIssuer = _config["Jwt__Issuer"] ?? "https://api.greenboard.local",
            ValidAudience = _config["Jwt__Audience"] ?? "greenboard-app",
            IssuerSigningKey = key
        };

        try
        {
            var principal = new JwtSecurityTokenHandler().ValidateToken(accessToken, validationParameters, out _);
            return principal;
        }
        catch
        {
            return null;
        }
    }
}
