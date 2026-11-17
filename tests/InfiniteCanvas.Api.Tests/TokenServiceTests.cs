using System.Security.Claims;
using System.Text;
using InfiniteCanvas.Api.Data;
using InfiniteCanvas.Api.Models;
using InfiniteCanvas.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace InfiniteCanvas.Api.Tests;

public class TokenServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly TokenService _service;
    private readonly AppUser _user;

    public TokenServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString("N"))
            .Options;
        _db = new AppDbContext(options);

        _user = new AppUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = "test@example.com",
            Email = "test@example.com",
            DisplayName = "Utilisateur Test",
            EmailConfirmed = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.Users.Add(_user);
        _db.SaveChanges();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt__Key"] = "test-key-32-chars-long-for-signing-tokens",
                ["Jwt__Issuer"] = "https://test.local",
                ["Jwt__Audience"] = "greenboard-app"
            })
            .Build();

        _service = new TokenService(_db, config, new StubWebHostEnvironment());
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void CreateAccessToken_ContientLesClaimsAttendus()
    {
        var token = _service.CreateAccessToken(_user);

        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-key-32-chars-long-for-signing-tokens"));
        var principal = handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = "https://test.local",
            ValidAudience = "greenboard-app",
            IssuerSigningKey = key,
            ClockSkew = TimeSpan.Zero
        }, out _);

        Assert.Equal(_user.Id, principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal(_user.Email, principal.FindFirst(ClaimTypes.Email)?.Value);
        Assert.Equal(_user.DisplayName, principal.FindFirst(ClaimTypes.Name)?.Value);
    }

    [Fact]
    public void CreateAccessToken_ExpireDans15Minutes()
    {
        var token = _service.CreateAccessToken(_user);

        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        var expiresAt = jwt.ValidTo;

        Assert.InRange(expiresAt - DateTime.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(16));
    }

    [Fact]
    public async Task SaveAndValidateRefreshToken_Fonctionne()
    {
        var token = _service.CreateRefreshToken(_user);
        await _service.SaveRefreshTokenAsync(_user.Id, token);

        var valide = await _service.ValidateRefreshTokenAsync(_user.Id, token);
        Assert.True(valide);
    }

    [Fact]
    public async Task ValidateRefreshToken_ToquenInvalide_RetourneFaux()
    {
        var valide = await _service.ValidateRefreshTokenAsync(_user.Id, "n'importe-quoi");
        Assert.False(valide);
    }

    [Fact]
    public async Task RevokeRefreshToken_LeTokenEstRefuse()
    {
        var token = _service.CreateRefreshToken(_user);
        await _service.SaveRefreshTokenAsync(_user.Id, token);

        await _service.RevokeRefreshTokenAsync(_user.Id, token);

        var valide = await _service.ValidateRefreshTokenAsync(_user.Id, token);
        Assert.False(valide);
    }

    [Fact]
    public async Task RevokeAllRefreshTokens_TousLesTokensSontRefuses()
    {
        var t1 = _service.CreateRefreshToken(_user);
        var t2 = _service.CreateRefreshToken(_user);
        await _service.SaveRefreshTokenAsync(_user.Id, t1);
        await _service.SaveRefreshTokenAsync(_user.Id, t2);

        await _service.RevokeAllRefreshTokensAsync(_user.Id);

        Assert.False(await _service.ValidateRefreshTokenAsync(_user.Id, t1));
        Assert.False(await _service.ValidateRefreshTokenAsync(_user.Id, t2));
    }

    [Fact]
    public void GetPrincipalFromExpiredToken_ExtraitLeClaimsMemeExpire()
    {
        var token = _service.CreateAccessToken(_user);
        var principal = _service.GetPrincipalFromExpiredToken(token);

        Assert.NotNull(principal);
        Assert.Equal(_user.Id, principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
    }

    [Fact]
    public void GetPrincipalFromExpiredToken_NulOuVide_RetourneNul()
    {
        Assert.Null(_service.GetPrincipalFromExpiredToken(null));
        Assert.Null(_service.GetPrincipalFromExpiredToken(""));
    }
}

// ---------- Doubles minimaux ----------

internal sealed class StubWebHostEnvironment : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "Test";
    public string? WebRootPath { get; set; }
    public string ContentRootPath { get; set; } = System.IO.Path.GetTempPath();
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
        new Microsoft.Extensions.FileProviders.PhysicalFileProvider(System.IO.Path.GetTempPath());
    public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } =
        new Microsoft.Extensions.FileProviders.PhysicalFileProvider(System.IO.Path.GetTempPath());
}
