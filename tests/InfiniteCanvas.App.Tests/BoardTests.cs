using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace InfiniteCanvas.App.Tests;

public sealed class BoardTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("greenboard-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>Les objets WPF (encre, rendu bitmap) exigent un thread STA.</summary>
    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw new Xunit.Sdk.XunitException($"Échec sur le thread STA : {error}");
    }

    private static Stroke MakeStroke(double x, double y, Color color)
    {
        var points = new StylusPointCollection
        {
            new StylusPoint(x, y), new StylusPoint(x + 80, y + 40), new StylusPoint(x + 160, y)
        };
        return new Stroke(points, new DrawingAttributes { Color = color, Width = 4, Height = 4 });
    }

    private static byte[] MakePng(int w, int h)
    {
        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null,
            Enumerable.Repeat((byte)0x80, w * h * 4).ToArray(), w * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    [Fact]
    public void Enregistrer_puis_ouvrir_restaure_les_pages_traits_images_fonds_et_vues() => Sta(() =>
    {
        var strokes = new StrokeCollection { MakeStroke(100, 100, Colors.Red), MakeStroke(300, 200, Colors.Blue) };
        var png = MakePng(20, 10);
        var page1 = new BoardPageData("p1", "Électricité : loi d'Ohm", strokes,
            [new BoardItemData("abc123", png, new Rect(10, 20, 200, 100), "Schéma")],
            BoardBackground.Seyes, 1.5, -1234, -567);
        var page2 = new BoardPageData("p2", "Exercices", new StrokeCollection { MakeStroke(0, 0, Colors.Green) }, [],
            BoardBackground.TableauVert, 0.5, -10, -20);

        string path = Path.Combine(_dir, "tableau.gboard");
        BoardFile.Save(path, new BoardDocument([page1, page2], ActivePage: 1));
        var loaded = BoardFile.Load(path);

        Assert.Equal(2, loaded.Pages.Count);
        Assert.Equal(1, loaded.ActivePage);

        var p1 = loaded.Pages[0];
        Assert.Equal("p1", p1.Id);
        Assert.Equal("Électricité : loi d'Ohm", p1.Title);
        Assert.Equal(2, p1.Strokes.Count);
        Assert.Equal(Colors.Red, p1.Strokes[0].DrawingAttributes.Color);
        Assert.Equal(strokes[1].StylusPoints.Count, p1.Strokes[1].StylusPoints.Count);
        var item = Assert.Single(p1.Items);
        Assert.Equal("abc123", item.Id);
        Assert.Equal("Schéma", item.Label);
        Assert.Equal(new Rect(10, 20, 200, 100), item.Bounds);
        Assert.Equal(png, item.Data);
        Assert.Equal(BoardBackground.Seyes, p1.Background);
        Assert.Equal(1.5, p1.Scale);
        Assert.Equal(-1234, p1.TranslateX);

        var p2 = loaded.Pages[1];
        Assert.Equal("Exercices", p2.Title);
        Assert.Single(p2.Strokes);
        Assert.Empty(p2.Items);
        Assert.Equal(BoardBackground.TableauVert, p2.Background);
        Assert.False(File.Exists(path + ".tmp"));
    });

    [Fact]
    public void Un_fichier_version_2_a_page_unique_reste_lisible() => Sta(() =>
    {
        // Archive au format de la version précédente : ink.isf + items/ + board.json à la racine
        string path = Path.Combine(_dir, "ancien.gboard");
        var png = MakePng(8, 8);
        using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
        {
            using (var ink = zip.CreateEntry("ink.isf").Open())
                new StrokeCollection { MakeStroke(5, 5, Colors.Black) }.Save(ink, true);
            using (var img = zip.CreateEntry("items/x1.png").Open())
                img.Write(png);
            using var json = new StreamWriter(zip.CreateEntry("board.json").Open());
            json.Write("""
                { "Version": 2, "App": "GreenBoard", "Background": "Quadrillage", "Scale": 2, "TranslateX": -50, "TranslateY": -60,
                  "Items": [ { "Id": "x1", "File": "items/x1.png", "Label": "Photo", "X": 1, "Y": 2, "Width": 30, "Height": 40 } ] }
                """);
        }

        var loaded = BoardFile.Load(path);

        var page = Assert.Single(loaded.Pages);
        Assert.Equal("Page 1", page.Title);
        Assert.Single(page.Strokes);
        Assert.Equal("Photo", Assert.Single(page.Items).Label);
        Assert.Equal(BoardBackground.Quadrillage, page.Background);
        Assert.Equal(2, page.Scale);
    });

    [Fact]
    public void Export_PDF_produit_une_page_par_page_du_tableau() => Sta(() =>
    {
        var pages = new List<BoardExporter.Page>
        {
            new(new StrokeCollection { MakeStroke(0, 0, Colors.Black) }, [], BoardBackground.Blanc),
            new(new StrokeCollection(), [], BoardBackground.Seyes), // page vide : page PDF au format A4
            new(new StrokeCollection { MakeStroke(500, 500, Colors.Red) }, [], BoardBackground.Millimetre)
        };
        string path = Path.Combine(_dir, "cours.pdf");
        BoardExporter.Export(path, pages, activePage: 0);

        string text = Encoding.Latin1.GetString(File.ReadAllBytes(path));
        Assert.Contains("/Count 3", text);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(text, @"/Type /Page /Parent").Count);
        Assert.Contains("/MediaBox [0 0 842.25 595.5]", text); // 1123 × 794 unités × 0,75
    });

    [Theory]
    [InlineData(".png", BoardBackground.Quadrillage)]
    [InlineData(".jpg", BoardBackground.TableauVert)]
    [InlineData(".svg", BoardBackground.Millimetre)]
    [InlineData(".pdf", BoardBackground.Seyes)]
    [InlineData(".pdf", BoardBackground.Blanc)]
    public void Export_produit_un_fichier_valide(string extension, BoardBackground background) => Sta(() =>
    {
        var strokes = new StrokeCollection { MakeStroke(100, 100, Colors.Black) };
        var png = MakePng(30, 20);
        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = new MemoryStream(png);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        var items = new List<BoardExporter.Item> { new(image, new Rect(150, 150, 120, 80), png) };

        string path = Path.Combine(_dir, "export" + extension);
        BoardExporter.Export(path, strokes, items, background);

        var bytes = File.ReadAllBytes(path);
        switch (extension)
        {
            case ".png":
                Assert.Equal(0x89, bytes[0]);
                var frame = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                // zone utile (100..270 × 100..230) + marge de 40, rendue en 2×
                Assert.InRange(frame.PixelWidth, 2 * 240, 2 * 260);
                break;
            case ".jpg":
                Assert.Equal(0xFF, bytes[0]);
                Assert.Equal(0xD8, bytes[1]);
                break;
            case ".svg":
                string svg = Encoding.UTF8.GetString(bytes);
                Assert.StartsWith("<svg", svg);
                Assert.Contains("<pattern", svg);
                Assert.Contains("data:image/png;base64,", svg);
                Assert.DoesNotContain(",5 ", svg.Replace("0,5", "")); // aucun nombre au format français (virgule décimale)
                System.Xml.Linq.XDocument.Parse(svg); // XML bien formé
                break;
            case ".pdf":
                string text = Encoding.Latin1.GetString(bytes);
                Assert.StartsWith("%PDF-1.4", text);
                Assert.EndsWith("%%EOF\n", text);
                Assert.Contains("/Subtype /Image", text);
                Assert.Equal(background != BoardBackground.Blanc, text.Contains("/PatternType 1"));
                // la table xref pointe exactement sur chaque objet
                int xref = int.Parse(text[(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0]);
                var offsets = text[xref..].Split('\n').Skip(3).TakeWhile(l => l.EndsWith(" n ")).Select(l => int.Parse(l[..10])).ToList();
                for (int i = 0; i < offsets.Count; i++)
                    Assert.StartsWith($"{i + 1} 0 obj", text[offsets[i]..]);
                break;
        }
    });

    [Fact]
    public void Destinataires_separes_valides_et_dedoublonnes()
    {
        var (valid, invalid) = MailShare.ParseRecipients(
            "prof@lycee.fr; eleve.un@ecole.org, Eleve.Un@ecole.org\n  parent@mail.com  pas-une-adresse  x@y");

        Assert.Equal(["prof@lycee.fr", "eleve.un@ecole.org", "parent@mail.com"], valid);
        Assert.Equal(["pas-une-adresse", "x@y"], invalid);
        Assert.Empty(MailShare.ParseRecipients("  ;, ").Valid);
    }

    [Fact]
    public void Lien_mailto_encode_destinataires_objet_et_message()
    {
        var request = new ShareRequest(["a@b.fr", "c@d.fr"], "Cours : l'atome & ses électrons", "Bonjour,\r\nCi-joint.",
            AllPages: true, FileName: "Cours");

        string link = MailShare.BuildMailto(request);

        Assert.StartsWith("mailto:a%40b.fr,c%40d.fr?subject=", link);
        Assert.Contains("Cours%20%3A%20l%27atome%20%26%20ses%20%C3%A9lectrons", link);
        Assert.Contains("&body=Bonjour%2C%0D%0ACi-joint.", link);
    }

    [Theory]
    [InlineData("Physique : atome?", "Physique _ atome_")]
    [InlineData("   ", "Tableau")]
    [InlineData("cours.", "cours")]
    public void Nom_de_fichier_sur(string input, string expected) => Assert.Equal(expected, MailShare.SafeFileName(input));

    [Fact]
    public void Annuler_et_retablir_couvrent_ajouts_gomme_deplacements_et_images() => Sta(() =>
    {
        var canvas = new InkCanvas();
        var history = new BoardHistory(canvas);
        history.Reset();

        var a = MakeStroke(0, 0, Colors.Black);
        var b = MakeStroke(50, 50, Colors.Black);
        canvas.Strokes.Add(a);
        canvas.Strokes.Add(b);
        Assert.True(history.CanUndo);

        // Un geste de gomme sur deux traits = une seule étape
        history.BeginGroup();
        canvas.Strokes.Remove(a);
        canvas.Strokes.Remove(b);
        history.EndGroup();
        Assert.Empty(canvas.Strokes);
        history.Undo();
        Assert.Equal(2, canvas.Strokes.Count);

        // Déplacement
        var before = a.StylusPoints.Clone();
        a.Transform(new Matrix(1, 0, 0, 1, 100, 0), false);
        history.RecordPoints([(a, before, a.StylusPoints.Clone())]);
        history.Undo();
        Assert.Equal(before[0].X, a.StylusPoints[0].X);
        history.Redo();
        Assert.Equal(before[0].X + 100, a.StylusPoints[0].X);

        // Image
        var image = new Image { Width = 10, Height = 10 };
        canvas.Children.Add(image);
        history.RecordElementAdded(image);
        history.Undo();
        Assert.Empty(canvas.Children);
        history.Redo();
        Assert.Single(canvas.Children);

        // Une nouvelle action vide la pile « Rétablir »
        history.Undo();
        canvas.Strokes.Add(MakeStroke(10, 10, Colors.Red));
        Assert.False(history.CanRedo);
    });
}
