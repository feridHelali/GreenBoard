using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using InfiniteCanvas.Api.Data;
using InfiniteCanvas.Api.Models;
using InfiniteCanvas.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ---------- Configuration ----------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings__DefaultConnection n'est pas d\u00e9fini.");
var jwtKey = builder.Configuration["Jwt__Key"]
    ?? throw new InvalidOperationException("Jwt__Key n'est pas d\u00e9fini.");
var jwtIssuer = builder.Configuration["Jwt__Issuer"] ?? "https://api.greenboard.local";
var jwtAudience = builder.Configuration["Jwt__Audience"] ?? "greenboard-app";

// ---------- Services ----------
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 12;
    options.User.RequireUniqueEmail = true;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.FromMinutes(1)
    };
});

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IDocumentStore, LocalDocumentStore>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();

var app = builder.Build();

// ---------- Middleware ----------
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.UseSwagger();
app.UseSwaggerUI();

// ---------- Health ----------
app.MapHealthChecks("/health");

// ---------- Auth endpoints ----------
app.MapPost("/api/v1/auth/register", async (
    RegisterRequest req,
    UserManager<AppUser> userManager,
    IEmailService emailService,
    ITokenService tokenService,
    HttpContext http) =>
{
    var user = new AppUser
    {
        UserName = req.Email,
        Email = req.Email,
        DisplayName = req.DisplayName ?? req.Email,
        EmailConfirmed = false,
        CreatedAt = DateTimeOffset.UtcNow
    };

    var result = await userManager.CreateAsync(user, req.Password);
    if (!result.Succeeded)
    {
        return Results.Problem(
            detail: string.Join("; ", result.Errors.Select(e => e.Description)),
            statusCode: StatusCodes.Status400BadRequest,
            title: "Inscription \u00e9chou\u00e9e",
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
    }

    var confirmToken = await userManager.GenerateEmailConfirmationTokenAsync(user);
    await emailService.SendConfirmationAsync(user.Email!, confirmToken);

    var accessToken = tokenService.CreateAccessToken(user);
    var refreshToken = tokenService.CreateRefreshToken(user);
    await tokenService.SaveRefreshTokenAsync(user.Id, refreshToken);

    return Results.Ok(new AuthResponse(
        AccessToken: accessToken,
        RefreshToken: refreshToken,
        ExpiresIn: 900,
        User: new UserInfo(user.Id, user.Email!, user.DisplayName!, user.EmailConfirmed)));
});

app.MapPost("/api/v1/auth/login", async (
    LoginRequest req,
    SignInManager<AppUser> signInManager,
    UserManager<AppUser> userManager,
    ITokenService tokenService) =>
{
    var user = await userManager.FindByEmailAsync(req.Email);
    if (user == null)
    {
        return Results.Problem(
            detail: "Identifiants incorrects.",
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Connexion \u00e9chou\u00e9e",
            type: "https://tools.ietf.org/html/rfc7235#section-3.1");
    }

    var result = await signInManager.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);
    if (!result.Succeeded)
    {
        return Results.Problem(
            detail: "Identifiants incorrects.",
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Connexion \u00e9chou\u00e9e",
            type: "https://tools.ietf.org/html/rfc7235#section-3.1");
    }

    var accessToken = tokenService.CreateAccessToken(user);
    var refreshToken = tokenService.CreateRefreshToken(user);
    await tokenService.SaveRefreshTokenAsync(user.Id, refreshToken);

    return Results.Ok(new AuthResponse(
        AccessToken: accessToken,
        RefreshToken: refreshToken,
        ExpiresIn: 900,
        User: new UserInfo(user.Id, user.Email!, user.DisplayName!, user.EmailConfirmed)));
});

app.MapPost("/api/v1/auth/refresh", async (
    RefreshRequest req,
    UserManager<AppUser> userManager,
    ITokenService tokenService) =>
{
    var principal = tokenService.GetPrincipalFromExpiredToken(req.AccessToken);
    var userId = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (userId == null)
    {
        return Results.Unauthorized();
    }

    var user = await userManager.FindByIdAsync(userId);
    if (user == null || !await tokenService.ValidateRefreshTokenAsync(userId, req.RefreshToken))
    {
        return Results.Unauthorized();
    }

    await tokenService.RevokeRefreshTokenAsync(userId, req.RefreshToken);
    var newAccessToken = tokenService.CreateAccessToken(user);
    var newRefreshToken = tokenService.CreateRefreshToken(user);
    await tokenService.SaveRefreshTokenAsync(user.Id, newRefreshToken);

    return Results.Ok(new AuthResponse(
        AccessToken: newAccessToken,
        RefreshToken: newRefreshToken,
        ExpiresIn: 900,
        User: new UserInfo(user.Id, user.Email!, user.DisplayName!, user.EmailConfirmed)));
});

app.MapPost("/api/v1/auth/logout", (HttpContext http, ITokenService tokenService) =>
{
    var userId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (userId != null)
    {
        tokenService.RevokeAllRefreshTokensAsync(userId).Wait();
    }
    return Results.Ok();
}).RequireAuthorization();

// ---------- Me ----------
app.MapGet("/api/v1/me", (ClaimsPrincipal user, AppDbContext db, IDocumentStore store) =>
{
    var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
    var appUser = db.Users.FirstOrDefault(u => u.Id == userId);
    if (appUser == null)
    {
        return Results.NotFound();
    }

    var used = store.GetUsedQuotaBytes(userId);
    return Results.Ok(new MeResponse(
        Id: appUser.Id,
        Email: appUser.Email!,
        DisplayName: appUser.DisplayName!,
        EmailConfirmed: appUser.EmailConfirmed,
        CreatedAt: appUser.CreatedAt,
        QuotaUsedBytes: used,
        QuotaTotalBytes: 500L * 1024 * 1024));
}).RequireAuthorization();

app.MapDelete("/api/v1/me", async (ClaimsPrincipal user, UserManager<AppUser> userManager, IDocumentStore store) =>
{
    var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
    var appUser = await userManager.FindByIdAsync(userId);
    if (appUser == null)
    {
        return Results.NotFound();
    }

    await store.DeleteUserDocumentsAsync(userId);
    var result = await userManager.DeleteAsync(appUser);
    if (!result.Succeeded)
    {
        return Results.Problem(
            detail: string.Join("; ", result.Errors.Select(e => e.Description)),
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Suppression du compte \u00e9chou\u00e9e");
    }

    return Results.Ok(new { message = "Compte supprim\u00e9 avec tous ses documents." });
}).RequireAuthorization();

// ---------- Documents ----------
app.MapGet("/api/v1/documents", (ClaimsPrincipal user, IDocumentStore store) =>
{
    var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
    var docs = store.ListDocuments(userId);
    return Results.Ok(docs.Select(d => new DocumentInfo(
        Id: d.Id,
        Title: d.Title,
        SizeBytes: d.SizeBytes,
        PageCount: d.PageCount,
        ModifiedAt: d.ModifiedAt,
        Version: d.Version)));
}).RequireAuthorization();

app.MapPost("/api/v1/documents", async (ClaimsPrincipal user, HttpContext http, IDocumentStore store, ILogger<Program> logger) =>
{
    var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
    var form = await http.Request.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file == null || file.Length == 0)
    {
        return Results.Problem(
            detail: "Aucun fichier fourni.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Fichier manquant");
    }
    if (file.Length > 50L * 1024 * 1024)
    {
        return Results.Problem(
            detail: "Le fichier d\u00e9passe 50 Mo.",
            statusCode: StatusCodes.Status413RequestEntityTooLarge,
            title: "Fichier trop volumineux");
    }

    using var ms = new MemoryStream();
    await file.CopyToAsync(ms);
    var gboardBytes = ms.ToArray();

    if (!IsValidGboard(gboardBytes, out var pageCount, out var title))
    {
        return Results.Problem(
            detail: "Fichier .gboard invalide : archive ZIP attendue avec board.json \u00e0 la racine.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Fichier invalide");
    }

    var doc = await store.SaveDocumentAsync(userId, title ?? file.FileName, gboardBytes, pageCount);
    return Results.Created($"/api/v1/documents/{doc.Id}", new DocumentInfo(
        Id: doc.Id,
        Title: doc.Title,
        SizeBytes: doc.SizeBytes,
        PageCount: doc.PageCount,
        ModifiedAt: doc.ModifiedAt,
        Version: doc.Version));
}).RequireAuthorization();

app.MapGet("/api/v1/documents/{id}", (ClaimsPrincipal user, string id, IDocumentStore store) =>
{
    var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
    var doc = store.GetDocument(userId, id);
    if (doc == null)
    {
        return Results.NotFound();
    }

    return Results.File(doc.Content, contentType: "application/octet-stream", fileDownloadName: doc.Title);
}).RequireAuthorization();

app.MapPut("/api/v1/documents/{id}", async (ClaimsPrincipal user, string id, HttpContext http, IDocumentStore store) =>
{
    var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
    var form = await http.Request.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file == null || file.Length == 0)
    {
        return Results.Problem(
            detail: "Aucun fichier fourni.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Fichier manquant");
    }

    var etag = http.Request.Headers.IfMatch.FirstOrDefault();
    if (string.IsNullOrEmpty(etag))
    {
        return Results.Problem(
            detail: "L'en-t\u00e8te If-Match est requise pour remplacer un document.",
            statusCode: StatusCodes.Status428PreconditionRequired,
            title: "Version manquante");
    }

    using var ms = new MemoryStream();
    await file.CopyToAsync(ms);
    var gboardBytes = ms.ToArray();

    if (!IsValidGboard(gboardBytes, out var pageCount, out var title))
    {
        return Results.Problem(
            detail: "Fichier .gboard invalide.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Fichier invalide");
    }

    var updated = await store.ReplaceDocumentAsync(userId, id, etag, title ?? file.FileName, gboardBytes, pageCount);
    if (updated == null)
    {
        return Results.Conflict(new { message = "Le document a \u00e9t\u00e9 modifi\u00e9 par un autre client. R\u00e9cup\u00e9rez la derni\u00e8re version et r\u00e9essayez." });
    }

    return Results.Ok(new DocumentInfo(
        Id: updated.Id,
        Title: updated.Title,
        SizeBytes: updated.SizeBytes,
        PageCount: updated.PageCount,
        ModifiedAt: updated.ModifiedAt,
        Version: updated.Version));
}).RequireAuthorization();

app.MapPatch("/api/v1/documents/{id}", async (ClaimsPrincipal user, string id, RenameRequest req, IDocumentStore store) =>
{
    var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
    var renamed = await store.RenameDocumentAsync(userId, id, req.Title);
    if (renamed == null)
    {
        return Results.NotFound();
    }
    return Results.Ok(new { id = renamed.Id, title = renamed.Title });
}).RequireAuthorization();

app.MapDelete("/api/v1/documents/{id}", async (ClaimsPrincipal user, string id, IDocumentStore store) =>
{
    var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
    var deleted = await store.DeleteDocumentAsync(userId, id);
    if (!deleted)
    {
        return Results.NotFound();
    }
    return Results.NoContent();
}).RequireAuthorization();

// ---------- Migrations au d\u00e9marrage ----------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();

// ---------- Validation .gboard ----------
static bool IsValidGboard(byte[] bytes, out int pageCount, out string? title)
{
    pageCount = 0;
    title = null;
    try
    {
        using var ms = new MemoryStream(bytes);
        using var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
        var boardEntry = zip.GetEntry("board.json");
        if (boardEntry == null)
        {
            return false;
        }
        using var reader = new StreamReader(boardEntry.Open());
        var json = reader.ReadToEnd();
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("pages", out var pages) && pages.ValueKind == JsonValueKind.Array)
        {
            pageCount = pages.GetArrayLength();
        }

        title = root.TryGetProperty("title", out var t) ? t.GetString() : null;
        return true;
    }
    catch
    {
        return false;
    }
}

// ---------- Records ----------
public record RegisterRequest(string Email, string Password, string? DisplayName = null);
public record LoginRequest(string Email, string Password);
public record RefreshRequest(string AccessToken, string RefreshToken);
public record RenameRequest(string Title);
public record AuthResponse(string AccessToken, string RefreshToken, int ExpiresIn, UserInfo User);
public record UserInfo(string Id, string Email, string DisplayName, bool EmailConfirmed);
public record MeResponse(string Id, string Email, string DisplayName, bool EmailConfirmed, DateTimeOffset CreatedAt, long QuotaUsedBytes, long QuotaTotalBytes);
public record DocumentInfo(string Id, string Title, long SizeBytes, int PageCount, DateTimeOffset ModifiedAt, string Version);
