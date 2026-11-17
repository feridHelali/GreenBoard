using System.Text.Json;
using InfiniteCanvas.Api.Data;
using InfiniteCanvas.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace InfiniteCanvas.Api.Tests;

public class LocalDocumentStoreTests : IDisposable
{
    private readonly string _root;
    private readonly AppDbContext _db;
    private readonly LocalDocumentStore _store;

    public LocalDocumentStoreTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GreenBoardApiTests", Guid.NewGuid().ToString("N"));

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString("N"))
            .Options;
        _db = new AppDbContext(options);

        var env = new MockWebHostEnvironmentForStore(_root);
        _store = new LocalDocumentStore(_db, env);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { System.IO.Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task SaveEtListDocument_ConcerneSeulementLeProprietaire()
    {
        var doc1 = await _store.SaveDocumentAsync("userA", "Mon tableau", System.Text.Encoding.UTF8.GetBytes("contenu"), 1);
        var doc2 = await _store.SaveDocumentAsync("userB", "Leur tableau", System.Text.Encoding.UTF8.GetBytes("autre"), 2);

        var docsA = _store.ListDocuments("userA").ToList();
        var docsB = _store.ListDocuments("userB").ToList();

        Assert.Single(docsA);
        Assert.Equal("Mon tableau", docsA[0].Title);
        Assert.Single(docsB);
        Assert.Equal("Leur tableau", docsB[0].Title);
    }

    [Fact]
    public async Task GetDocument_VerifieLaPropriete()
    {
        var doc = await _store.SaveDocumentAsync("userA", "Tableau A", System.Text.Encoding.UTF8.GetBytes("contenu"), 1);

        var recupere = _store.GetDocument("userA", doc.Id);
        Assert.NotNull(recupere);
        Assert.Equal("Tableau A", recupere.Title);

        var interdit = _store.GetDocument("userB", doc.Id);
        Assert.Null(interdit);
    }

    [Fact]
    public async Task RenameDocument_ModifieLeTitre()
    {
        var doc = await _store.SaveDocumentAsync("userA", "Ancien titre", System.Text.Encoding.UTF8.GetBytes("contenu"), 1);

        var renomme = await _store.RenameDocumentAsync("userA", doc.Id, "Nouveau titre");
        Assert.NotNull(renomme);
        Assert.Equal("Nouveau titre", renomme.Title);

        var recupere = _store.GetDocument("userA", doc.Id);
        Assert.Equal("Nouveau titre", recupere.Title);
    }

    [Fact]
    public async Task DeleteDocument_SupprimeEtNeRendPlusRien()
    {
        var doc = await _store.SaveDocumentAsync("userA", "À supprimer", System.Text.Encoding.UTF8.GetBytes("contenu"), 1);

        var resultat = await _store.DeleteDocumentAsync("userA", doc.Id);
        Assert.True(resultat);

        var recupere = _store.GetDocument("userA", doc.Id);
        Assert.Null(recupere);

        // Un autre utilisateur ne peut pas supprimer ce document
        var resultatInterdit = await _store.DeleteDocumentAsync("userB", doc.Id);
        Assert.False(resultatInterdit);
    }

    [Fact]
    public async Task DeleteUserDocuments_SupprimeToutPourUnUtilisateur()
    {
        await _store.SaveDocumentAsync("userA", "Doc 1", System.Text.Encoding.UTF8.GetBytes("c1"), 1);
        await _store.SaveDocumentAsync("userA", "Doc 2", System.Text.Encoding.UTF8.GetBytes("c2"), 2);
        await _store.SaveDocumentAsync("userB", "Doc B", System.Text.Encoding.UTF8.GetBytes("cb"), 1);

        await _store.DeleteUserDocumentsAsync("userA");

        Assert.Empty(_store.ListDocuments("userA"));
        Assert.Single(_store.ListDocuments("userB"));
    }

    [Fact]
    public async Task GetUsedQuotaBytes_RetourneLaTailleTotale()
    {
        await _store.SaveDocumentAsync("userA", "Doc 1", System.Text.Encoding.UTF8.GetBytes("abcdefghij"), 1); // 10 octets
        await _store.SaveDocumentAsync("userA", "Doc 2", System.Text.Encoding.UTF8.GetBytes("12345"), 1); // 5 octets

        var quota = _store.GetUsedQuotaBytes("userA");
        Assert.Equal(15, quota);

        var quotaB = _store.GetUsedQuotaBytes("userB");
        Assert.Equal(0, quotaB);
    }

    [Fact]
    public async Task ReplaceDocument_ETagCorrespond_MetAJour()
    {
        var doc = await _store.SaveDocumentAsync("userA", "Titre", System.Text.Encoding.UTF8.GetBytes("v1"), 1);

        var updated = await _store.ReplaceDocumentAsync("userA", doc.Id, doc.Version, "Titre modifié", System.Text.Encoding.UTF8.GetBytes("v2"), 1);
        Assert.NotNull(updated);
        Assert.Equal("Titre modifié", updated.Title);
        Assert.NotEqual(doc.Version, updated.Version);
    }

    [Fact]
    public async Task ReplaceDocument_ETagInvalide_RetourneNull()
    {
        var doc = await _store.SaveDocumentAsync("userA", "Titre", System.Text.Encoding.UTF8.GetBytes("v1"), 1);

        var updated = await _store.ReplaceDocumentAsync("userA", doc.Id, "mauvais-etag", "Titre", System.Text.Encoding.UTF8.GetBytes("v2"), 1);
        Assert.Null(updated);
    }
}

// ---------- Doubles ----------

internal sealed class MockWebHostEnvironmentForStore : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
{
    private string _contentRoot;

    public MockWebHostEnvironmentForStore(string contentRoot) => _contentRoot = contentRoot;

    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "Test";
    public string? WebRootPath { get; set; }
    public string ContentRootPath { get => _contentRoot; set => _contentRoot = value; }
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
        new Microsoft.Extensions.FileProviders.PhysicalFileProvider(System.IO.Path.GetTempPath());
    public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } =
        new Microsoft.Extensions.FileProviders.PhysicalFileProvider(System.IO.Path.GetTempPath());
}
