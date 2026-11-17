namespace InfiniteCanvas.Api.Services;

public interface ITokenService
{
    string CreateAccessToken(Models.AppUser user);
    string CreateRefreshToken(Models.AppUser user);
    Task SaveRefreshTokenAsync(string userId, string refreshToken);
    Task<bool> ValidateRefreshTokenAsync(string userId, string refreshToken);
    Task RevokeRefreshTokenAsync(string userId, string refreshToken);
    Task RevokeAllRefreshTokensAsync(string userId);
    System.Security.Claims.ClaimsPrincipal? GetPrincipalFromExpiredToken(string? accessToken);
}
