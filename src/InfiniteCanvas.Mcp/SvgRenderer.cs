using System.Text.RegularExpressions;
using SkiaSharp;
using Svg.Skia;

namespace InfiniteCanvas.Mcp;

/// <summary>Rendu SVG → PNG (SkiaSharp), avec garde-fous sur le contenu fourni par le modèle.</summary>
public static partial class SvgRenderer
{
    /// <summary>Résultat : PNG + taille logique de l'illustration (unités SVG = unités du tableau à 100 %).</summary>
    public sealed record Rendered(byte[] Png, double Width, double Height, int PixelWidth, int PixelHeight);

    private const int MaxPixelSide = 4096;

    // Toute référence externe est refusée : seuls les liens internes (#id) et les données embarquées (data:) sont permis
    [GeneratedRegex("""(?:xlink:)?href\s*=\s*["']\s*(?!#|data:)""", RegexOptions.IgnoreCase)]
    private static partial Regex ExternalHref();

    [GeneratedRegex("""<!DOCTYPE|<!ENTITY|<script|@import|url\(\s*["']?\s*(?!#|data:)""", RegexOptions.IgnoreCase)]
    private static partial Regex Forbidden();

    /// <summary>
    /// Rend le SVG. <paramref name="oversample"/> = résolution par unité (3 → net jusqu'à ~300 % de zoom) ;
    /// <paramref name="maxSide"/> limite le plus grand côté en pixels (aperçu pour le modèle).
    /// </summary>
    public static Rendered Render(string svg, float oversample = 3f, int maxSide = MaxPixelSide)
    {
        if (string.IsNullOrWhiteSpace(svg) || !svg.Contains("<svg", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Le contenu doit être un document SVG complet (<svg ...>...</svg>).");
        if (ExternalHref().IsMatch(svg) || Forbidden().IsMatch(svg))
            throw new ArgumentException(
                "SVG refusé : pas de DOCTYPE/ENTITY, de <script>, d'@import ni de ressource externe. " +
                "Utilisez uniquement des éléments SVG autonomes (références internes #id et data: autorisées).");

        using var document = new SKSvg();
        var picture = document.FromSvg(svg) ?? throw new ArgumentException("SVG illisible.");
        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new ArgumentException("SVG sans dimensions : indiquez un viewBox (ex. viewBox=\"0 0 1000 700\").");

        float scale = Math.Min(oversample, maxSide / Math.Max(bounds.Width, bounds.Height));
        int pxW = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale));
        int pxH = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));

        using var surface = SKSurface.Create(new SKImageInfo(pxW, pxH, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scale);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);
        canvas.Flush();

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return new Rendered(data.ToArray(), bounds.Width, bounds.Height, pxW, pxH);
    }
}
