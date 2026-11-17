using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using InfiniteCanvas.Api.Models;

namespace InfiniteCanvas.Api.Data;

public sealed class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<RefreshToken> RefreshTokens { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AppUser>(entity =>
        {
            entity.Property(u => u.DisplayName).HasMaxLength(200);
            entity.Property(u => u.CreatedAt).IsRequired();
        });

        builder.ApplyConfiguration(new RefreshTokenConfiguration());

        // S\u00e9parer les tables Identity du pr\u00e9fixe AspNet par d\u00e9faut pour lisibilit\u00e9
        builder.Entity<AppUser>().ToTable("Users");
    }
}
