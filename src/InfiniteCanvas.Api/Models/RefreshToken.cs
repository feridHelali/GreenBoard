namespace InfiniteCanvas.Api.Models;

public sealed class RefreshToken
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? Revoked { get; set; }
    public string? ReplacedByToken { get; set; }
}
