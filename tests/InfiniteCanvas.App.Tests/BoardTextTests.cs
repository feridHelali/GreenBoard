using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;

namespace InfiniteCanvas.App.Tests;

public sealed class BoardTextTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("greenboard-text-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

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

    private static BoardTextData Sample(string text = "Théorème de Pythagore\na² + b² = c²") =>
        new("t1", text, new Rect(120, 80, 300, 70), 28, Color.FromRgb(0xE5, 0x39, 0x35));

    [Fact]
    public void Les_contours_remplissent_le_rectangle_du_texte() => Sta(() =>
    {
        var text = Sample();
        var bounds = BoardText.PlacedGeometry(text).Bounds;

        Assert.Equal(text.Bounds.X, bounds.X, 3);
        Assert.Equal(text.Bounds.Y, bounds.Y, 3);
        Assert.Equal(text.Bounds.Width, bounds.Width, 3);
        Assert.Equal(text.Bounds.Height, bounds.Height, 3);
    });

    [Fact]
    public void Un_texte_plus_grand_donne_des_contours_plus_grands() => Sta(() =>
    {
        var small = BoardText.BuildGeometry("Bonjour", 20).Bounds;
        var large = BoardText.BuildGeometry("Bonjour", 40).Bounds;
        Assert.Equal(2, large.Width / small.Width, 1);
    });

    [Fact]
    public void Enregistrer_puis_ouvrir_conserve_les_textes() => Sta(() =>
    {
        var text = Sample();
        var page = new BoardPageData("p1", "Cours", new StrokeCollection(), [], BoardBackground.TableauVert, 1, 0, 0, [text]);
        string path = Path.Combine(_dir, "texte.gboard");

        BoardFile.Save(path, new BoardDocument([page], 0));
        var loaded = BoardFile.Load(path).Pages[0].Texts!;

        var t = Assert.Single(loaded);
        Assert.Equal(text.Id, t.Id);
        Assert.Equal(text.Text, t.Text);           // retours à la ligne et caractères accentués conservés
        Assert.Equal(text.Bounds, t.Bounds);
        Assert.Equal(text.FontSize, t.FontSize);
        Assert.Equal(text.Color, t.Color);
    });

    [Fact]
    public void Une_page_sans_texte_reste_compatible() => Sta(() =>
    {
        var page = new BoardPageData("p1", "Vide", new StrokeCollection(), [], BoardBackground.Blanc, 1, 0, 0);
        string path = Path.Combine(_dir, "sans-texte.gboard");
        BoardFile.Save(path, new BoardDocument([page], 0));
        Assert.Empty(BoardFile.Load(path).Pages[0].Texts!);
    });

    [Fact]
    public void Une_page_avec_seulement_du_texte_s_exporte_en_vectoriel() => Sta(() =>
    {
        var text = Sample("Ohm : U = R × I");
        var page = new BoardExporter.Page(new StrokeCollection(), [], BoardBackground.Blanc, [text]);
        Assert.False(page.IsEmpty);

        string svg = Path.Combine(_dir, "texte.svg");
        BoardExporter.Export(svg, [page], 0);
        Assert.Contains("fill=\"#E53935\"", File.ReadAllText(svg)); // contours du texte, couleur conservée

        string pdf = Path.Combine(_dir, "texte.pdf");
        BoardExporter.Export(pdf, [page], 0);
        string content = Encoding.Latin1.GetString(File.ReadAllBytes(pdf));
        Assert.StartsWith("%PDF-", content);
        Assert.Contains("/Count 1", content);

        string png = Path.Combine(_dir, "texte.png");
        BoardExporter.Export(png, [page], 0);
        Assert.True(new FileInfo(png).Length > 1000);
    });
}
