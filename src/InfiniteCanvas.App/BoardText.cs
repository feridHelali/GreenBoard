using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace InfiniteCanvas.App;

/// <summary>
/// Texte posé sur le tableau. <paramref name="Bounds"/> = rectangle occupé par les glyphes (coordonnées monde) ;
/// <paramref name="FontSize"/> = taille de saisie (le redimensionnement étire le texte dans <paramref name="Bounds"/>).
/// </summary>
public sealed record BoardTextData(string Id, string Text, Rect Bounds, double FontSize, Color Color);

/// <summary>
/// Le texte est affiché et exporté sous forme de contours vectoriels (comme l'encre) : rendu identique à l'écran,
/// en PNG, SVG et PDF, sans police à embarquer.
/// </summary>
public static class BoardText
{
    public const string FontFamilyName = "Segoe UI";
    public const double MinFontSize = 8, MaxFontSize = 200;

    private static readonly Typeface Face = new(new FontFamily(FontFamilyName), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    /// <summary>Contours des glyphes, origine (0,0) = haut gauche de la première ligne.</summary>
    public static Geometry BuildGeometry(string text, double fontSize)
    {
        var ft = new FormattedText(text, CultureInfo.GetCultureInfo("fr-FR"), FlowDirection.LeftToRight, Face,
            Math.Clamp(fontSize, MinFontSize, MaxFontSize), Brushes.Black, 1.0);
        var geometry = ft.BuildGeometry(new Point(0, 0));
        geometry.Freeze();
        return geometry;
    }

    /// <summary>Contours placés dans <see cref="BoardTextData.Bounds"/> (étirés comme à l'écran).</summary>
    public static Geometry PlacedGeometry(BoardTextData text)
    {
        var geometry = BuildGeometry(text.Text, text.FontSize);
        var gb = geometry.Bounds;
        if (gb.IsEmpty || gb.Width <= 0 || gb.Height <= 0) return Geometry.Empty;

        var m = Matrix.Identity;
        m.Translate(-gb.X, -gb.Y);
        m.Scale(text.Bounds.Width / gb.Width, text.Bounds.Height / gb.Height);
        m.Translate(text.Bounds.X, text.Bounds.Y);

        var placed = geometry.Clone();
        placed.Transform = new MatrixTransform(m);
        placed.Freeze();
        return placed;
    }
}
