using Microsoft.AspNetCore.Identity;

namespace InfiniteCanvas.Api.Models;

public sealed class AppUser : IdentityUser
{
    public string? DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
