using InfiniteCanvas.Api.Models;

namespace InfiniteCanvas.Api.Services;

/// <summary>
/// Abstraction de stockage des documents .gboard.
/// Permet de changer de fournisseur (disque local pour le d\u00e9veloppement,
/// stockage objet S3-compatible en production) sans modifier l'API.
/// </summary>
public interface IDocumentStore
{
    /// <summary>Liste les documents d'un utilisateur.</summary>
    IEnumerable<DocumentEntry> ListDocuments(string userId);

    /// <summary>R\u00e9cup\u00e9re un document (v\u00e9rifie la propri\u00e9t\u00e9).</summary>
    DocumentContent? GetDocument(string userId, string documentId);

    /// <summary>Enregistre un nouveau document.</summary>
    Task<DocumentEntry> SaveDocumentAsync(string userId, string title, byte[] content, int pageCount);

    /// <summary>Remplace un document avec contr\u00f4le de version optimiste (ETag).</summary>
    Task<DocumentEntry?> ReplaceDocumentAsync(string userId, string documentId, string currentETag, string title, byte[] content, int pageCount);

    /// <summary>Renomme un document.</summary>
    Task<DocumentEntry?> RenameDocumentAsync(string userId, string documentId, string newTitle);

    /// <summary>Supprime un document (v\u00e9rifie la propri\u00e9t\u00e9).</summary>
    Task<bool> DeleteDocumentAsync(string userId, string documentId);

    /// <summary>Supprime tous les documents d'un utilisateur (RGPD).</summary>
    Task DeleteUserDocumentsAsync(string userId);

    /// <summary>Quota utilis\u00e9 en octets.</summary>
    long GetUsedQuotaBytes(string userId);
}

public sealed record DocumentEntry(
    string Id,
    string Title,
    long SizeBytes,
    int PageCount,
    DateTimeOffset ModifiedAt,
    string Version);

public sealed record DocumentContent(
    string Id,
    string Title,
    byte[] Content,
    string Version,
    DateTimeOffset ModifiedAt);
