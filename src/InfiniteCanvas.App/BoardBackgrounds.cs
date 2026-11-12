using System.Windows;
using System.Windows.Media;

namespace InfiniteCanvas.App;

/// <summary>Les 6 arrière-plans proposés.</summary>
public enum BoardBackground { Blanc, TableauVert, Quadrillage, Millimetre, Seyes, Pointilles }

/// <summary>
/// Fabrique des motifs d'arrière-plan en coordonnées « monde ».
/// Partagée par l'écran (motif infini qui suit la vue) et par l'export (PNG/JPEG/SVG/PDF).
/// </summary>
public static class BoardBackgrounds
{
    public static string Label(BoardBackground bg) => bg switch
    {
        BoardBackground.Blanc => "Blanc",
        BoardBackground.TableauVert => "Tableau vert",
        BoardBackground.Quadrillage => "Quadrillage",
        BoardBackground.Millimetre => "Papier millimétré",
        BoardBackground.Seyes => "Cahier Seyès",
        BoardBackground.Pointilles => "Pointillés",
        _ => bg.ToString()
    };

    public static bool IsDark(BoardBackground bg) => bg == BoardBackground.TableauVert;

    /// <summary>
    /// Pinceau d'arrière-plan pour une échelle d'affichage donnée : l'épaisseur des traits est
    /// compensée pour rester constante à l'écran. Renvoie un <see cref="SolidColorBrush"/> (gelé)
    /// ou un <see cref="DrawingBrush"/> en mosaïque (non gelé, pour pouvoir lui donner une Transform).
    /// </summary>
    public static Brush CreateBrush(BoardBackground bg, double scale)
    {
        // Épaisseur en unités monde pour obtenir « px » pixels à l'écran
        double Px(double px) => px / scale;

        switch (bg)
        {
            case BoardBackground.Blanc:
                return Frozen("#FFFFFF");

            case BoardBackground.TableauVert:
                return Frozen("#2F4F3E");

            case BoardBackground.Quadrillage:
            {
                const double size = 40;
                if (size * scale < 8) return Frozen("#FFFFFF");
                return Tile(size, "#FFFFFF", dc =>
                {
                    var line = Frozen("#DCE3EC");
                    dc.DrawRectangle(line, null, new Rect(0, 0, size, Px(1)));
                    dc.DrawRectangle(line, null, new Rect(0, 0, Px(1), size));
                });
            }

            case BoardBackground.Millimetre:
            {
                const double size = 50; // 1 cm = 50 unités, 1 mm = 5 unités
                if (size * scale < 8) return Frozen("#FFFDF6");
                return Tile(size, "#FFFDF6", dc =>
                {
                    if (5 * scale >= 4)
                    {
                        var minor = Frozen("#F7D9C4");
                        for (int i = 1; i < 10; i++)
                        {
                            if (i == 5) continue;
                            dc.DrawRectangle(minor, null, new Rect(0, i * 5, size, Px(0.6)));
                            dc.DrawRectangle(minor, null, new Rect(i * 5, 0, Px(0.6), size));
                        }
                    }
                    if (25 * scale >= 6)
                    {
                        var mid = Frozen("#F0BE9C");
                        dc.DrawRectangle(mid, null, new Rect(0, 25, size, Px(0.9)));
                        dc.DrawRectangle(mid, null, new Rect(25, 0, Px(0.9), size));
                    }
                    var major = Frozen("#E39A6E");
                    dc.DrawRectangle(major, null, new Rect(0, 0, size, Px(1.3)));
                    dc.DrawRectangle(major, null, new Rect(0, 0, Px(1.3), size));
                });
            }

            case BoardBackground.Seyes:
            {
                const double size = 32; // grand carreau de 8 mm, 4 interlignes de 2 mm
                if (size * scale < 8) return Frozen("#FFFFFF");
                return Tile(size, "#FFFFFF", dc =>
                {
                    if (8 * scale >= 4)
                    {
                        var minor = Frozen("#CBD8F0");
                        for (int i = 1; i < 4; i++)
                            dc.DrawRectangle(minor, null, new Rect(0, i * 8, size, Px(0.7)));
                    }
                    dc.DrawRectangle(Frozen("#8DA8DA"), null, new Rect(0, 0, size, Px(1.1)));
                    dc.DrawRectangle(Frozen("#B7A3D9"), null, new Rect(0, 0, Px(1), size));
                });
            }

            case BoardBackground.Pointilles:
            {
                const double size = 28;
                if (size * scale < 8) return Frozen("#FAFAF9");
                return Tile(size, "#FAFAF9", dc =>
                {
                    double r = Math.Min(Px(1.4), size / 4);
                    dc.DrawEllipse(Frozen("#B4BAC4"), null, new Point(size / 2, size / 2), r, r);
                });
            }
        }
        return Frozen("#FFFFFF");
    }

    private static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush(Hex(hex));
        brush.Freeze();
        return brush;
    }

    private static DrawingBrush Tile(double size, string baseHex, Action<DrawingContext> draw)
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            // Fond plein sur toute la tuile : fixe aussi les bornes exactes du motif
            dc.DrawRectangle(Frozen(baseHex), null, new Rect(0, 0, size, size));
            draw(dc);
        }
        group.Freeze();

        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, size, size),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, size, size),
            ViewboxUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill
        };
    }
}
