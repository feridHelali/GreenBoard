using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;

namespace InfiniteCanvas.App;

/// <summary>Image posée sur le tableau (PNG/JPEG importé ou illustration générée via MCP).</summary>
public sealed record BoardItemData(string Id, byte[] Data, Rect Bounds, string? Label);

/// <summary>Une page du classeur : ses traits, ses images, son arrière-plan, sa vue et ses textes.</summary>
public sealed record BoardPageData(
    string Id,
    string Title,
    StrokeCollection Strokes,
    IReadOnlyList<BoardItemData> Items,
    BoardBackground Background,
    double Scale,
    double TranslateX,
    double TranslateY,
    IReadOnlyList<BoardTextData>? Texts = null);

/// <summary>Contenu d'un fichier : les pages, dans l'ordre des onglets, et la page active.</summary>
public sealed record BoardDocument(IReadOnlyList<BoardPageData> Pages, int ActivePage);

/// <summary>
/// Format de fichier « .gboard » : une archive ZIP contenant
///  - <c>pages/&lt;n&gt;/ink.isf</c>   : les traits de la page n (Ink Serialized Format natif : pression, couleurs…) ;
///  - <c>pages/&lt;n&gt;/items/*</c>   : les images de la page n, octets d'origine ;
///  - <c>board.json</c>             : métadonnées (version, page active, titre/arrière-plan/vue/images/textes de chaque page).
/// Version 4 : ajout des textes (« Texts » dans chaque page).
/// Les versions 1 et 2 (une seule page, à la racine de l'archive) restent lisibles.
/// En attendant la base SQLite prévue par le cahier des charges.
/// </summary>
public static class BoardFile
{
    public const string Extension = ".gboard";
    public const string DialogFilter = "Tableau GreenBoard (*.gboard)|*.gboard";
    private const int CurrentVersion = 4;

    private sealed class Meta
    {
        public int Version { get; set; } = CurrentVersion;
        public string App { get; set; } = "GreenBoard";
        public int ActivePage { get; set; }
        public List<PageMeta>? Pages { get; set; }

        // Champs des versions 1 et 2 (page unique), lus pour compatibilité
        public BoardBackground Background { get; set; }
        public double Scale { get; set; } = 1;
        public double TranslateX { get; set; }
        public double TranslateY { get; set; }
        public List<ItemMeta>? Items { get; set; }
    }

    private sealed class PageMeta
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Ink { get; set; } = "";
        public BoardBackground Background { get; set; }
        public double Scale { get; set; } = 1;
        public double TranslateX { get; set; }
        public double TranslateY { get; set; }
        public List<ItemMeta> Items { get; set; } = [];
        public List<TextMeta>? Texts { get; set; }
    }

    private sealed class TextMeta
    {
        public string Id { get; set; } = "";
        public string Text { get; set; } = "";
        public double FontSize { get; set; } = 28;
        /// <summary>Couleur « #AARRGGBB ».</summary>
        public string Color { get; set; } = "#FF1F2328";
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    private sealed class ItemMeta
    {
        public string Id { get; set; } = "";
        public string File { get; set; } = "";
        public string? Label { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Save(string path, BoardDocument doc)
    {
        // Écriture dans un fichier temporaire puis remplacement : jamais de fichier à moitié écrit
        string temp = path + ".tmp";
        using (var file = File.Create(temp))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        {
            var meta = new Meta { ActivePage = doc.ActivePage, Pages = [] };

            for (int p = 0; p < doc.Pages.Count; p++)
            {
                var page = doc.Pages[p];
                string folder = $"pages/{p}";
                var pageMeta = new PageMeta
                {
                    Id = page.Id, Title = page.Title, Ink = $"{folder}/ink.isf",
                    Background = page.Background, Scale = page.Scale,
                    TranslateX = page.TranslateX, TranslateY = page.TranslateY
                };

                using (var ink = zip.CreateEntry(pageMeta.Ink, CompressionLevel.Optimal).Open())
                    page.Strokes.Save(ink, compress: true);

                for (int i = 0; i < page.Items.Count; i++)
                {
                    var item = page.Items[i];
                    string name = $"{folder}/items/{i}{ImageExtension(item.Data)}";
                    // Les PNG/JPEG sont déjà compressés : stockage direct
                    using (var entry = zip.CreateEntry(name, CompressionLevel.NoCompression).Open())
                        entry.Write(item.Data);
                    pageMeta.Items.Add(new ItemMeta
                    {
                        Id = item.Id, File = name, Label = item.Label,
                        X = item.Bounds.X, Y = item.Bounds.Y, Width = item.Bounds.Width, Height = item.Bounds.Height
                    });
                }
                if (page.Texts is { Count: > 0 } texts)
                    pageMeta.Texts = texts.Select(t => new TextMeta
                    {
                        Id = t.Id, Text = t.Text, FontSize = t.FontSize, Color = t.Color.ToString(CultureInfo.InvariantCulture),
                        X = t.Bounds.X, Y = t.Bounds.Y, Width = t.Bounds.Width, Height = t.Bounds.Height
                    }).ToList();
                meta.Pages.Add(pageMeta);
            }

            using var json = zip.CreateEntry("board.json", CompressionLevel.Optimal).Open();
            JsonSerializer.Serialize(json, meta, JsonOptions);
        }
        File.Move(temp, path, overwrite: true);
    }

    public static BoardDocument Load(string path)
    {
        using var zip = ZipFile.OpenRead(path);

        var metaEntry = zip.GetEntry("board.json") ?? throw new InvalidDataException("board.json manquant.");
        Meta meta;
        using (var json = metaEntry.Open())
            meta = JsonSerializer.Deserialize<Meta>(json, JsonOptions) ?? new Meta();
        if (meta.Version > CurrentVersion)
            throw new InvalidDataException("Ce fichier a été créé par une version plus récente de GreenBoard.");

        var pages = new List<BoardPageData>();
        if (meta.Pages is { Count: > 0 })
        {
            foreach (var p in meta.Pages)
                pages.Add(new BoardPageData(
                    string.IsNullOrEmpty(p.Id) ? NewId() : p.Id,
                    string.IsNullOrWhiteSpace(p.Title) ? $"Page {pages.Count + 1}" : p.Title,
                    ReadStrokes(zip, p.Ink), ReadItems(zip, p.Items),
                    p.Background, p.Scale, p.TranslateX, p.TranslateY, ReadTexts(p.Texts)));
        }
        else
        {
            // Versions 1 et 2 : une seule page à la racine de l'archive
            pages.Add(new BoardPageData(NewId(), "Page 1",
                ReadStrokes(zip, "ink.isf"), ReadItems(zip, meta.Items ?? []),
                meta.Background, meta.Scale, meta.TranslateX, meta.TranslateY));
        }

        return new BoardDocument(pages, Math.Clamp(meta.ActivePage, 0, pages.Count - 1));
    }

    public static string NewId() => Guid.NewGuid().ToString("N")[..12];

    /// <summary>Charge un document .gboard depuis un tableau d'octets (ex. téléchargement cloud).</summary>
    public static BoardDocument LoadFromBytes(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        return Load(zip);
    }

    /// <summary>Sérialise un document en tableau d'octets .gboard (ex. envoi cloud).</summary>
    public static byte[] SerializeToBytes(BoardDocument doc)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create))
        {
            var meta = new Meta { ActivePage = doc.ActivePage, Pages = [] };

            for (int p = 0; p < doc.Pages.Count; p++)
            {
                var page = doc.Pages[p];
                string folder = $"pages/{p}";
                var pageMeta = new PageMeta
                {
                    Id = page.Id, Title = page.Title, Ink = $"{folder}/ink.isf",
                    Background = page.Background, Scale = page.Scale,
                    TranslateX = page.TranslateX, TranslateY = page.TranslateY
                };

                using (var ink = zip.CreateEntry(pageMeta.Ink, CompressionLevel.Optimal).Open())
                    page.Strokes.Save(ink, compress: true);

                for (int i = 0; i < page.Items.Count; i++)
                {
                    var item = page.Items[i];
                    string name = $"{folder}/items/{i}{ImageExtension(item.Data)}";
                    using (var entry = zip.CreateEntry(name, CompressionLevel.NoCompression).Open())
                        entry.Write(item.Data);
                    pageMeta.Items.Add(new ItemMeta
                    {
                        Id = item.Id, File = name, Label = item.Label,
                        X = item.Bounds.X, Y = item.Bounds.Y, Width = item.Bounds.Width, Height = item.Bounds.Height
                    });
                }
                if (page.Texts is { Count: > 0 } texts)
                    pageMeta.Texts = texts.Select(t => new TextMeta
                    {
                        Id = t.Id, Text = t.Text, FontSize = t.FontSize,
                        Color = t.Color.ToString(CultureInfo.InvariantCulture),
                        X = t.Bounds.X, Y = t.Bounds.Y, Width = t.Bounds.Width, Height = t.Bounds.Height
                    }).ToList();
                meta.Pages.Add(pageMeta);
            }

            using var json = zip.CreateEntry("board.json", CompressionLevel.Optimal).Open();
            JsonSerializer.Serialize(json, meta, JsonOptions);
        }
        return ms.ToArray();
    }

    private static BoardDocument Load(ZipArchive zip)
    {
        var metaEntry = zip.GetEntry("board.json") ?? throw new InvalidDataException("board.json manquant.");
        Meta meta;
        using (var json = metaEntry.Open())
            meta = JsonSerializer.Deserialize<Meta>(json, JsonOptions) ?? new Meta();
        if (meta.Version > CurrentVersion)
            throw new InvalidDataException("Ce fichier a été créé par une version plus récente de GreenBoard.");

        var pages = new List<BoardPageData>();
        if (meta.Pages is { Count: > 0 })
        {
            foreach (var p in meta.Pages)
                pages.Add(new BoardPageData(
                    string.IsNullOrEmpty(p.Id) ? NewId() : p.Id,
                    string.IsNullOrWhiteSpace(p.Title) ? $"Page {pages.Count + 1}" : p.Title,
                    ReadStrokes(zip, p.Ink), ReadItems(zip, p.Items),
                    p.Background, p.Scale, p.TranslateX, p.TranslateY, ReadTexts(p.Texts)));
        }
        else
        {
            // Versions 1 et 2 : une seule page à la racine de l'archive
            pages.Add(new BoardPageData(NewId(), "Page 1",
                ReadStrokes(zip, "ink.isf"), ReadItems(zip, meta.Items ?? []),
                meta.Background, meta.Scale, meta.TranslateX, meta.TranslateY));
        }

        return new BoardDocument(pages, Math.Clamp(meta.ActivePage, 0, pages.Count - 1));
    }

    private static StrokeCollection ReadStrokes(ZipArchive zip, string entryName)
    {
        var entry = zip.GetEntry(entryName);
        if (entry == null) return new StrokeCollection();
        var bytes = ReadAll(entry);
        // Le lecteur ISF exige un flux positionnable
        return bytes.Length == 0 ? new StrokeCollection() : new StrokeCollection(new MemoryStream(bytes));
    }

    private static List<BoardItemData> ReadItems(ZipArchive zip, IEnumerable<ItemMeta> items)
    {
        var result = new List<BoardItemData>();
        foreach (var m in items)
        {
            var entry = zip.GetEntry(m.File);
            if (entry == null) continue; // image manquante : on charge le reste de la page
            result.Add(new BoardItemData(m.Id, ReadAll(entry), new Rect(m.X, m.Y, m.Width, m.Height), m.Label));
        }
        return result;
    }

    private static List<BoardTextData> ReadTexts(IEnumerable<TextMeta>? texts)
    {
        var result = new List<BoardTextData>();
        foreach (var t in texts ?? [])
        {
            if (string.IsNullOrWhiteSpace(t.Text) || !(t.Width > 0) || !(t.Height > 0)) continue;
            Color color;
            try { color = (Color)ColorConverter.ConvertFromString(t.Color); }
            catch (FormatException) { color = Colors.Black; }
            result.Add(new BoardTextData(string.IsNullOrEmpty(t.Id) ? NewId() : t.Id, t.Text,
                new Rect(t.X, t.Y, t.Width, t.Height), t.FontSize, color));
        }
        return result;
    }

    private static byte[] ReadAll(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>Extension déduite de la signature du fichier (PNG ou JPEG).</summary>
    public static string ImageExtension(byte[] data) =>
        data.Length > 3 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 ? ".png"
        : data.Length > 2 && data[0] == 0xFF && data[1] == 0xD8 ? ".jpg"
        : ".bin";
}
