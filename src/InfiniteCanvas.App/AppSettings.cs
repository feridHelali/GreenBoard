using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InfiniteCanvas.App;

/// <summary>
/// Réglages de l'utilisateur (roue dentée en bas à gauche), enregistrés dans
/// <c>%APPDATA%\GreenBoard\reglages.json</c>. Un fichier absent ou illisible redonne les valeurs par défaut.
/// </summary>
public sealed record AppSettings
{
    public const double MinThickness = 1, MaxThickness = 20;
    public const double MinZoomStep = 1.02, MaxZoomStep = 1.5;
    public const double MinTextSize = 12, MaxTextSize = 120;
    public static readonly int[] AutoSaveChoices = [0, 1, 2, 5, 10];

    /// <summary>Épaisseur du stylet (main libre).</summary>
    public double PenThickness { get; init; } = 3;
    /// <summary>Épaisseur des formes (rectangle, ellipse, triangle, ligne, flèche).</summary>
    public double ShapeThickness { get; init; } = 3;
    /// <summary>Lissage du tracé en courbes de Bézier.</summary>
    public bool PenSmoothing { get; init; } = true;
    /// <summary>Épaisseur variable selon la pression du stylet graphique.</summary>
    public bool PenPressure { get; init; } = true;
    /// <summary>Taille des nouveaux textes (outil Texte).</summary>
    public double TextSize { get; init; } = 28;
    /// <summary>Arrière-plan des nouveaux tableaux.</summary>
    public BoardBackground DefaultBackground { get; init; } = BoardBackground.Blanc;
    /// <summary>Facteur de zoom par cran de molette.</summary>
    public double WheelZoomStep { get; init; } = 1.1;
    /// <summary>Demander confirmation avant « Tout effacer ».</summary>
    public bool ConfirmClear { get; init; } = true;
    /// <summary>Sauvegarde automatique d'un tableau déjà enregistré, en minutes (0 = désactivée).</summary>
    public int AutoSaveMinutes { get; init; }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GreenBoard", "reglages.json");

    /// <summary>Ramène chaque valeur dans ses bornes (fichier modifié à la main, ancienne version…).</summary>
    public AppSettings Normalized() => this with
    {
        PenThickness = Clamp(PenThickness, MinThickness, MaxThickness, 3),
        ShapeThickness = Clamp(ShapeThickness, MinThickness, MaxThickness, 3),
        TextSize = Clamp(TextSize, MinTextSize, MaxTextSize, 28),
        DefaultBackground = Enum.IsDefined(DefaultBackground) ? DefaultBackground : BoardBackground.Blanc,
        WheelZoomStep = Clamp(WheelZoomStep, MinZoomStep, MaxZoomStep, 1.1),
        AutoSaveMinutes = AutoSaveChoices.Contains(AutoSaveMinutes) ? AutoSaveMinutes : 0
    };

    private static double Clamp(double v, double min, double max, double fallback) =>
        double.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;

    public static AppSettings Load(string? path = null)
    {
        try
        {
            path ??= DefaultPath;
            if (!File.Exists(path)) return new AppSettings();
            return (JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new AppSettings()).Normalized();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return new AppSettings();
        }
    }

    /// <summary>Enregistre les réglages ; renvoie faux si le disque refuse (les réglages restent actifs pour la session).</summary>
    public bool Save(string? path = null)
    {
        try
        {
            path ??= DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(Normalized(), Json));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
