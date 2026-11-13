using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace InfiniteCanvas.App;

/// <summary>
/// Export du tableau (zone utile = traits + marge) en PNG, JPEG, SVG ou PDF.
/// SVG et PDF sont vectoriels : chaque trait est exporté avec son contour réel (pression, lissage).
/// </summary>
public static class BoardExporter
{
    public const string DialogFilter =
        "Image PNG (*.png)|*.png|Image JPEG (*.jpg)|*.jpg|Vectoriel SVG (*.svg)|*.svg|Document PDF (*.pdf)|*.pdf";

    private const double Margin = 40;
    private const double FlattenTolerance = 0.1;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Image du tableau à exporter : image décodée, position, octets d'origine (PNG/JPEG).</summary>
    public sealed record Item(BitmapSource Image, Rect Bounds, byte[] Data);

    /// <summary>Une page à exporter.</summary>
    public sealed record Page(StrokeCollection Strokes, IReadOnlyList<Item> Items, BoardBackground Background,
        IReadOnlyList<BoardTextData>? Texts = null)
    {
        public bool IsEmpty => Strokes.Count == 0 && Items.Count == 0 && TextList.Count == 0;
        public IReadOnlyList<BoardTextData> TextList => Texts ?? [];
    }

    /// <summary>
    /// PDF : toutes les pages (une page PDF par page du classeur).
    /// PNG / JPEG / SVG : uniquement la page <paramref name="activePage"/>.
    /// </summary>
    public static void Export(string path, IReadOnlyList<Page> pages, int activePage)
    {
        var page = pages[activePage];
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".png": ExportBitmap(path, page, new PngBitmapEncoder()); break;
            case ".jpg":
            case ".jpeg": ExportBitmap(path, page, new JpegBitmapEncoder { QualityLevel = 92 }); break;
            case ".svg": File.WriteAllText(path, BuildSvg(page), new UTF8Encoding(false)); break;
            case ".pdf": File.WriteAllBytes(path, BuildPdf(pages)); break;
            default: throw new NotSupportedException($"Format non pris en charge : {Path.GetExtension(path)}");
        }
    }

    /// <summary>Exporte une seule page (raccourci).</summary>
    public static void Export(string path, StrokeCollection strokes, IReadOnlyList<Item> items, BoardBackground background) =>
        Export(path, [new Page(strokes, items, background)], 0);

    private static Rect ContentBounds(Page page)
    {
        // Page vide : format paysage A4 (à 96 ppp) pour garder une page PDF exploitable
        if (page.IsEmpty) return new Rect(0, 0, 1123, 794);

        Rect b = page.Strokes.GetBounds(); // Rect.Empty s'il n'y a aucun trait
        foreach (var item in page.Items) b.Union(item.Bounds);
        foreach (var text in page.TextList) b.Union(text.Bounds);
        b.Inflate(Margin, Margin);
        return new Rect(Math.Floor(b.X), Math.Floor(b.Y), Math.Ceiling(b.Width), Math.Ceiling(b.Height));
    }

    // =====================================================================
    // PNG / JPEG
    // =====================================================================
    private static void ExportBitmap(string path, Page page, BitmapEncoder encoder)
    {
        var (strokes, items, bg) = (page.Strokes, page.Items, page.Background);
        var bounds = ContentBounds(page);
        // 2× pour la netteté, plafonné à ~40 mégapixels
        double factor = Math.Min(2.0, Math.Sqrt(40_000_000 / (bounds.Width * bounds.Height)));
        int pxW = Math.Max(1, (int)Math.Ceiling(bounds.Width * factor));
        int pxH = Math.Max(1, (int)Math.Ceiling(bounds.Height * factor));

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(factor, factor));
            dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
            dc.DrawRectangle(BoardBackgrounds.CreateBrush(bg, factor), null, bounds);
            // Comme à l'écran : les images sous l'encre (on peut annoter une illustration)
            foreach (var item in items) dc.DrawImage(item.Image, item.Bounds);
            // Textes : au-dessus des images, sous l'encre (comme à l'écran, où ils sont avant les traits)
            foreach (var text in page.TextList) dc.DrawGeometry(new SolidColorBrush(text.Color), null, BoardText.PlacedGeometry(text));
            strokes.Draw(dc);
        }

        var bitmap = new RenderTargetBitmap(pxW, pxH, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    // =====================================================================
    // SVG
    // =====================================================================
    private static string BuildSvg(Page page)
    {
        var (strokes, items, bg) = (page.Strokes, page.Items, page.Background);
        var b = ContentBounds(page);
        var sb = new StringBuilder();
        sb.Append(Inv, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(b.Width)}\" height=\"{F(b.Height)}\" viewBox=\"{F(b.X)} {F(b.Y)} {F(b.Width)} {F(b.Height)}\">\n");
        sb.Append("<title>GreenBoard</title>\n");

        // Arrière-plan : couleur unie ou <pattern> reconstruit depuis la tuile du motif
        var brush = BoardBackgrounds.CreateBrush(bg, 1);
        string fill;
        if (brush is DrawingBrush tile)
        {
            double size = tile.Viewport.Width;
            sb.Append(Inv, $"<defs><pattern id=\"bg\" patternUnits=\"userSpaceOnUse\" x=\"0\" y=\"0\" width=\"{F(size)}\" height=\"{F(size)}\">\n");
            foreach (var (geometry, color) in Walk(tile.Drawing))
                sb.Append(Inv, $"<path d=\"{SvgPath(geometry)}\" fill=\"{Hex(color)}\"/>\n");
            sb.Append("</pattern></defs>\n");
            fill = "url(#bg)";
        }
        else
        {
            fill = Hex(((SolidColorBrush)brush).Color);
        }
        sb.Append(Inv, $"<rect x=\"{F(b.X)}\" y=\"{F(b.Y)}\" width=\"{F(b.Width)}\" height=\"{F(b.Height)}\" fill=\"{fill}\"/>\n");

        foreach (var item in items)
        {
            string mime = BoardFile.ImageExtension(item.Data) == ".jpg" ? "image/jpeg" : "image/png";
            sb.Append(Inv, $"<image x=\"{F(item.Bounds.X)}\" y=\"{F(item.Bounds.Y)}\" width=\"{F(item.Bounds.Width)}\" height=\"{F(item.Bounds.Height)}\" preserveAspectRatio=\"none\" href=\"data:{mime};base64,{Convert.ToBase64String(item.Data)}\"/>\n");
        }

        foreach (var text in page.TextList)
            sb.Append(Inv, $"<path d=\"{SvgPath(BoardText.PlacedGeometry(text))}\" fill=\"{Hex(text.Color)}\"/>\n");

        foreach (var stroke in strokes)
        {
            var c = stroke.DrawingAttributes.Color;
            string opacity = c.A < 255 ? $" fill-opacity=\"{F(c.A / 255.0)}\"" : "";
            sb.Append(Inv, $"<path d=\"{SvgPath(stroke.GetGeometry())}\" fill=\"{Hex(c)}\"{opacity}/>\n");
        }

        sb.Append("</svg>\n");
        return sb.ToString();
    }

    private static string SvgPath(Geometry geometry)
    {
        var sb = new StringBuilder();
        foreach (var figure in Flatten(geometry))
        {
            for (int i = 0; i < figure.Count; i++)
                sb.Append(i == 0 ? "M" : "L").Append(F(figure[i].X)).Append(',').Append(F(figure[i].Y)).Append(' ');
            sb.Append("Z ");
        }
        return sb.ToString().TrimEnd();
    }

    // =====================================================================
    // PDF (vectoriel, écrit à la main : PDF 1.4, une page PDF par page, motif de fond en « tiling pattern »)
    // =====================================================================
    private static byte[] BuildPdf(IReadOnlyList<Page> pages)
    {
        // Objets numérotés à partir de 1 : index + 1. 1 = catalogue, 2 = arbre des pages (complété à la fin)
        var objects = new List<string> { "<< /Type /Catalog /Pages 2 0 R >>", "" };
        var kids = new List<int>();
        foreach (var page in pages)
            kids.Add(AppendPdfPage(objects, page));
        objects[1] = $"<< /Type /Pages /Kids [{string.Join(" ", kids.Select(k => $"{k} 0 R"))}] /Count {kids.Count} >>";
        return WritePdf(objects);
    }

    /// <summary>Ajoute la page, son contenu, son motif et ses images ; renvoie le numéro d'objet de la page.</summary>
    private static int AppendPdfPage(List<string> objects, Page page)
    {
        var (strokes, items, bg) = (page.Strokes, page.Items, page.Background);
        var b = ContentBounds(page);
        const double k = 0.75; // 1 unité WPF (1/96 po) = 0,75 point PDF (1/72 po)
        double pageW = b.Width * k, pageH = b.Height * k;

        var content = new StringBuilder();
        // Repère WPF (y vers le bas) → repère PDF (y vers le haut), origine sur le coin de la zone exportée
        content.Append(Inv, $"{F(k)} 0 0 {F(-k)} 0 {F(pageH)} cm 1 0 0 1 {F(-b.X)} {F(-b.Y)} cm\n");

        var brush = BoardBackgrounds.CreateBrush(bg, 1);
        string? patternStream = null;
        double tileSize = 0;
        if (brush is DrawingBrush tile)
        {
            tileSize = tile.Viewport.Width;
            var tileContent = new StringBuilder();
            foreach (var (geometry, color) in Walk(tile.Drawing))
                AppendPdfFill(tileContent, geometry, color);
            patternStream = tileContent.ToString();
            content.Append("/Pattern cs /P1 scn\n");
        }
        else
        {
            content.Append(PdfColor(((SolidColorBrush)brush).Color)).Append(" rg\n");
        }
        content.Append(Inv, $"{F(b.X)} {F(b.Y)} {F(b.Width)} {F(b.Height)} re f\n");

        // Images : dessinées sous l'encre. Dans notre repère (y vers le bas), l'image unité est retournée.
        for (int i = 0; i < items.Count; i++)
        {
            var r = items[i].Bounds;
            content.Append(Inv, $"q {F(r.Width)} 0 0 {F(-r.Height)} {F(r.X)} {F(r.Y + r.Height)} cm /Im{i + 1} Do Q\n");
        }

        foreach (var text in page.TextList)
            AppendPdfFill(content, BoardText.PlacedGeometry(text), text.Color);
        foreach (var stroke in strokes)
            AppendPdfFill(content, stroke.GetGeometry(), stroke.DrawingAttributes.Color);

        // --- Objets de la page : page (complétée une fois les ressources connues), contenu, motif, images ---
        int pageIndex = objects.Count;
        objects.Add("");
        objects.Add(Stream("", content.ToString()));
        int contentRef = objects.Count;

        var resources = new StringBuilder();
        if (patternStream != null)
        {
            // La matrice du motif s'exprime dans l'espace par défaut de la page : même transformation que le contenu
            string matrix = $"[{F(k)} 0 0 {F(-k)} {F(-k * b.X)} {F(k * (b.Height + b.Y))}]";
            objects.Add(Stream(
                $"/Type /Pattern /PatternType 1 /PaintType 1 /TilingType 1 /BBox [0 0 {F(tileSize)} {F(tileSize)}] " +
                $"/XStep {F(tileSize)} /YStep {F(tileSize)} /Resources << >> /Matrix {matrix} ",
                patternStream));
            resources.Append($"/Pattern << /P1 {objects.Count} 0 R >> ");
        }

        if (items.Count > 0)
        {
            resources.Append("/XObject << ");
            for (int i = 0; i < items.Count; i++)
            {
                var (rgb, alpha, w, h) = Pixels(items[i].Image);
                objects.Add(Stream($"/Type /XObject /Subtype /Image /Width {w} /Height {h} /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode ", Deflate(alpha)));
                int maskRef = objects.Count;
                objects.Add(Stream($"/Type /XObject /Subtype /Image /Width {w} /Height {h} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /SMask {maskRef} 0 R ", Deflate(rgb)));
                resources.Append($"/Im{i + 1} {objects.Count} 0 R ");
            }
            resources.Append(">> ");
        }

        objects[pageIndex] = $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(pageW)} {F(pageH)}] /Resources << {resources}>> /Contents {contentRef} 0 R >>";
        return pageIndex + 1;
    }

    private static byte[] WritePdf(List<string> objects)
    {
        var latin1 = Encoding.Latin1;
        using var ms = new MemoryStream();
        void Write(string s) { var bytes = latin1.GetBytes(s); ms.Write(bytes, 0, bytes.Length); }

        Write("%PDF-1.4\n%âãÏÓ\n");
        var offsets = new List<long>();
        for (int i = 0; i < objects.Count; i++)
        {
            offsets.Add(ms.Position);
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        long xref = ms.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Write($"{o:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R /Info << /Producer (GreenBoard) >> >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    private static string Stream(string dict, string data) =>
        $"<< {dict}/Length {Encoding.Latin1.GetByteCount(data)} >>\nstream\n{data}\nendstream";

    /// <summary>Pixels RVB + alpha séparés (pour /SMask), en 8 bits par composante.</summary>
    private static (byte[] Rgb, byte[] Alpha, int Width, int Height) Pixels(BitmapSource source)
    {
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight;
        var raw = new byte[w * h * 4];
        bgra.CopyPixels(raw, w * 4, 0);

        var rgb = new byte[w * h * 3];
        var alpha = new byte[w * h];
        for (int p = 0; p < w * h; p++)
        {
            rgb[p * 3] = raw[p * 4 + 2];
            rgb[p * 3 + 1] = raw[p * 4 + 1];
            rgb[p * 3 + 2] = raw[p * 4];
            alpha[p] = raw[p * 4 + 3];
        }
        return (rgb, alpha, w, h);
    }

    /// <summary>Compression zlib (FlateDecode), renvoyée en chaîne Latin-1 octet pour octet.</summary>
    private static string Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var z = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(data, 0, data.Length);
        return Encoding.Latin1.GetString(output.ToArray());
    }

    private static void AppendPdfFill(StringBuilder sb, Geometry geometry, Color color)
    {
        var figures = Flatten(geometry);
        if (figures.Count == 0) return;
        sb.Append(PdfColor(color)).Append(" rg\n");
        foreach (var figure in figures)
        {
            for (int i = 0; i < figure.Count; i++)
                sb.Append(F(figure[i].X)).Append(' ').Append(F(figure[i].Y)).Append(i == 0 ? " m\n" : " l\n");
            sb.Append("h\n");
        }
        sb.Append("f\n");
    }

    private static string PdfColor(Color c) => $"{F(c.R / 255.0)} {F(c.G / 255.0)} {F(c.B / 255.0)}";

    // =====================================================================
    // Outils communs
    // =====================================================================

    /// <summary>Aplatit une géométrie en figures (polygones) : seuls des segments de droite subsistent.</summary>
    private static List<List<Point>> Flatten(Geometry geometry)
    {
        var result = new List<List<Point>>();
        var flat = geometry.GetFlattenedPathGeometry(FlattenTolerance, ToleranceType.Absolute);
        foreach (var figure in flat.Figures)
        {
            var points = new List<Point> { figure.StartPoint };
            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case LineSegment line: points.Add(line.Point); break;
                    case PolyLineSegment poly: points.AddRange(poly.Points); break;
                }
            }
            if (points.Count > 1) result.Add(points);
        }
        return result;
    }

    /// <summary>Parcourt un dessin de tuile et renvoie ses formes pleines (géométrie + couleur).</summary>
    private static IEnumerable<(Geometry Geometry, Color Color)> Walk(Drawing drawing)
    {
        switch (drawing)
        {
            case DrawingGroup group:
                foreach (var child in group.Children)
                    foreach (var item in Walk(child))
                        yield return item;
                break;
            case GeometryDrawing { Geometry: { } g, Brush: SolidColorBrush brush }:
                yield return (g, brush.Color);
                break;
        }
    }

    private static string F(double v) => Math.Round(v, 3).ToString("0.###", Inv);
    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
