using System.Security.Cryptography;
using InfiniteCanvas.Api.Data;
using InfiniteCanvas.Api.Models;

namespace InfiniteCanvas.Api.Services;

/// <summary>
/// Impl\u00e9mentation disque local de <see cref="IDocumentStore"/> pour le d\u00e9veloppement.
/// En production, rempla\u00e7ez par une impl\u00e9mentation S3-compatible (Cloudflare R2, Backblaze B2, AWS S3).
/// </summary>
public sealed class LocalDocumentStore : IDocumentStore
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;

    public LocalDocumentStore(AppDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    private string RootPath => Path.Combine(_env.ContentRootPath, "data", "documents");

    public IEnumerable<DocumentEntry> ListDocuments(string userId)
    {
        var userDir = Path.Combine(RootPath, userId);
        if (!Directory.Exists(userDir))
        {
            return Enumerable.Empty<DocumentEntry>();
        }

        var result = new List<DocumentEntry>();
        foreach (var docDir in Directory.GetDirectories(userDir))
        {
            var versions = Directory.GetFiles(docDir, "*.gboard")
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f));
            var latest = versions.FirstOrDefault();
            if (latest == null)
            {
                continue;
            }

            var info = new FileInfo(latest);
            var metaPath = Path.Combine(docDir, "meta.json");
            string title = Path.GetFileNameWithoutExtension(latest);
            long size = info.Length;
            if (File.Exists(metaPath))
            {
                var meta = System.Text.Json.JsonSerializer.Deserialize<DocumentMeta>(File.ReadAllText(metaPath));
                if (meta != null)
                {
                    title = meta.Title;
                    size = meta.SizeBytes;
                }
            }

            result.Add(new DocumentEntry(
                Id: Path.GetFileName(docDir)!,
                Title: title,
                SizeBytes: size,
                PageCount: 0,
                ModifiedAt: DateTime.SpecifyKind(File.GetLastWriteTimeUtc(latest), DateTimeKind.Utc),
                Version: ComputeVersion(latest)));
        }

        return result.OrderByDescending(d => d.ModifiedAt);
    }

    public DocumentContent? GetDocument(string userId, string documentId)
    {
        var docDir = Path.Combine(RootPath, userId, documentId);
        if (!Directory.Exists(docDir))
        {
            return null;
        }

        var latest = Directory.GetFiles(docDir, "*.gboard")
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
            .FirstOrDefault();
        if (latest == null)
        {
            return null;
        }

        var metaPath = Path.Combine(docDir, "meta.json");
        string title = Path.GetFileNameWithoutExtension(latest);
        if (File.Exists(metaPath))
        {
            var meta = System.Text.Json.JsonSerializer.Deserialize<DocumentMeta>(File.ReadAllText(metaPath));
            if (meta != null)
            {
                title = meta.Title;
            }
        }

        return new DocumentContent(
            Id: documentId,
            Title: title,
            Content: File.ReadAllBytes(latest),
            Version: ComputeVersion(latest),
            ModifiedAt: DateTime.SpecifyKind(File.GetLastWriteTimeUtc(latest), DateTimeKind.Utc));
    }

    public async Task<DocumentEntry> SaveDocumentAsync(string userId, string title, byte[] content, int pageCount)
    {
        var documentId = Guid.NewGuid().ToString("N");
        var docDir = Path.Combine(RootPath, userId, documentId);
        Directory.CreateDirectory(docDir);

        var filePath = Path.Combine(docDir, $"{documentId}.gboard");
        await File.WriteAllBytesAsync(filePath, content);

        var meta = new DocumentMeta
        {
            Title = title,
            SizeBytes = content.Length,
            PageCount = pageCount,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await File.WriteAllTextAsync(
            Path.Combine(docDir, "meta.json"),
            System.Text.Json.JsonSerializer.Serialize(meta));

        return new DocumentEntry(
            Id: documentId,
            Title: title,
            SizeBytes: content.Length,
            PageCount: pageCount,
            ModifiedAt: meta.CreatedAt,
            Version: ComputeVersion(filePath));
    }

    public async Task<DocumentEntry?> ReplaceDocumentAsync(
        string userId, string documentId, string currentETag, string title, byte[] content, int pageCount)
    {
        var docDir = Path.Combine(RootPath, userId, documentId);
        if (!Directory.Exists(docDir))
        {
            return null;
        }

        var latest = Directory.GetFiles(docDir, "*.gboard")
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
            .FirstOrDefault();
        if (latest == null)
        {
            return null;
        }

        // Contr\u00f4le de version optimiste : l'ETag actuel doit correspondre \u00e0 la derni\u00e8re version
        var currentVersion = ComputeVersion(latest);
        if (!currentETag.Trim('"').Equals(currentVersion, StringComparison.OrdinalIgnoreCase))
        {
            return null; // 409 Conflict
        }

        var versionSuffix = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
        var newFilePath = Path.Combine(docDir, $"{documentId}_{versionSuffix}.gboard");
        await File.WriteAllBytesAsync(newFilePath, content);

        var meta = new DocumentMeta
        {
            Title = title,
            SizeBytes = content.Length,
            PageCount = pageCount,
            CreatedAt = DateTimeOffset.UtcNow,
            PreviousVersion = currentVersion
        };
        await File.WriteAllTextAsync(
            Path.Combine(docDir, "meta.json"),
            System.Text.Json.JsonSerializer.Serialize(meta));

        return new DocumentEntry(
            Id: documentId,
            Title: title,
            SizeBytes: content.Length,
            PageCount: pageCount,
            ModifiedAt: meta.CreatedAt,
            Version: ComputeVersion(newFilePath));
    }

    public async Task<DocumentEntry?> RenameDocumentAsync(string userId, string documentId, string newTitle)
    {
        var docDir = Path.Combine(RootPath, userId, documentId);
        if (!Directory.Exists(docDir))
        {
            return null;
        }

        var metaPath = Path.Combine(docDir, "meta.json");
        DocumentMeta meta;
        if (File.Exists(metaPath))
        {
            meta = System.Text.Json.JsonSerializer.Deserialize<DocumentMeta>(File.ReadAllText(metaPath))
                ?? new DocumentMeta();
        }
        else
        {
            meta = new DocumentMeta();
        }

        meta.Title = newTitle;
        meta.UpdatedAt = DateTimeOffset.UtcNow;
        await File.WriteAllTextAsync(metaPath, System.Text.Json.JsonSerializer.Serialize(meta));

        var latest = Directory.GetFiles(docDir, "*.gboard")
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
            .FirstOrDefault();
        if (latest == null)
        {
            return null;
        }

        return new DocumentEntry(
            Id: documentId,
            Title: newTitle,
            SizeBytes: new FileInfo(latest).Length,
            PageCount: meta.PageCount,
            ModifiedAt: meta.UpdatedAt,
            Version: ComputeVersion(latest));
    }

    public Task<bool> DeleteDocumentAsync(string userId, string documentId)
    {
        var docDir = Path.Combine(RootPath, userId, documentId);
        if (!Directory.Exists(docDir))
        {
            return Task.FromResult(false);
        }

        Directory.Delete(docDir, recursive: true);
        return Task.FromResult(true);
    }

    public Task DeleteUserDocumentsAsync(string userId)
    {
        var userDir = Path.Combine(RootPath, userId);
        if (Directory.Exists(userDir))
        {
            Directory.Delete(userDir, recursive: true);
        }
        return Task.CompletedTask;
    }

    public long GetUsedQuotaBytes(string userId)
    {
        var userDir = Path.Combine(RootPath, userId);
        if (!Directory.Exists(userDir))
        {
            return 0;
        }

        return Directory.GetFiles(userDir, "*.gboard", SearchOption.AllDirectories)
            .Sum(f => new FileInfo(f).Length);
    }

    private static string ComputeVersion(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha.ComputeHash(stream);
        return Convert.ToBase64String(hash);
    }

    private sealed class DocumentMeta
    {
        public string Title { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public int PageCount { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string? PreviousVersion { get; set; }
    }
}
