using System.IO;
using System.Text.Json;

namespace InfiniteCanvas.App;

/// <summary>Caractères autorisés dans une clé de licence (O, 0, I, L exclus).</summary>
internal static class LicenceAlphabet
{
    public const string Chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    public static readonly char[] Array = Chars.ToCharArray();
}

/// <summary>
/// Gestion du système de licences GreenBoard.
/// - Clé valide : illimité
/// - Pas de clé : mode démo 30 jours à partir du premier lancement
/// Après 30 jours sans clé, l'application est bloquée.
/// </summary>
public static class LicenseManager
{
    private const int DemoDays = 30;
    private const string AppDataFolder = "GreenBoard";
    private const string LicenceFileName = "licence.json";

    public static string LicencePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppDataFolder, LicenceFileName);

    public sealed record LicenceData(string? Key, DateTime? ActivatedAt, DateTime? FirstRun);

    /// <summary>Valide le format et la somme de contrôle d'une clé de licence.</summary>
    public static bool IsValidKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var k = key.Trim().ToUpperInvariant();
        if (!k.StartsWith("GB-")) return false;
        var groups = k.Substring(3).Split('-');
        if (groups.Length != 4) return false;
        foreach (var g in groups)
        {
            if (g.Length != 5) return false;
            // Le 5e caractère de chaque groupe est la somme de contrôle des 4 premiers
            var sum = 0;
            for (var i = 0; i < 4; i++)
            {
                var idx = LicenceAlphabet.Chars.IndexOf(g[i]);
                if (idx < 0) return false;
                sum += idx;
            }
            var expected = LicenceAlphabet.Array[sum % LicenceAlphabet.Chars.Length];
            if (g[4] != expected) return false;
        }
        return true;
    }

    /// <summary>Génère une clé de licence valide (pour tests ou administration).</summary>
    public static string GenerateKey()
    {
        var rng = new System.Random();
        var parts = new string[4];
        for (var p = 0; p < 4; p++)
        {
            var group = "";
            for (var i = 0; i < 4; i++)
                group += LicenceAlphabet.Array[rng.Next(0, LicenceAlphabet.Array.Length)];
            var sum = 0;
            foreach (var c in group)
                sum += LicenceAlphabet.Chars.IndexOf(c);
            group += LicenceAlphabet.Array[sum % LicenceAlphabet.Chars.Length];
            parts[p] = group;
        }
        return "GB-" + string.Join("-", parts);
    }

    /// <summary>Charge la licence stockée (ou null si absente / illisible).</summary>
    public static LicenceData? Load()
    {
        try
        {
            if (!File.Exists(LicencePath)) return null;
            var json = File.ReadAllText(LicencePath);
            return JsonSerializer.Deserialize<LicenceData>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Enregistre une licence (clé + date d'activation).</summary>
    public static void Save(string key)
    {
        var dir = Path.GetDirectoryName(LicencePath)!;
        Directory.CreateDirectory(dir);
        var data = new LicenceData(key, DateTimeOffset.UtcNow.DateTime, DateTimeOffset.UtcNow.DateTime);
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(LicencePath, json);
    }

    /// <summary>
    /// Détermine si l'application peut démarrer.
    /// Retourne (autorisé, message) où message est vide si autorisé,
    /// ou contient le texte à afficher à l'utilisateur sinon.
    /// </summary>
    public static (bool Allowed, string Message) Check()
    {
        var data = Load();

        // Clé valide : accès illimité
        if (data is { Key: { } k } && IsValidKey(k))
        {
            return (true, "");
        }

        // Pas de clé valide : mode démo
        DateTime firstRunUtc;
        if (data == null || data.FirstRun == null)
        {
            // Tout premier lancement : enregistrer la date
            SaveDemoFirstRun();
            firstRunUtc = DateTime.UtcNow;
        }
        else
        {
            firstRunUtc = data.FirstRun.Value;
        }

        var daysLeft = DemoDays - (DateTime.UtcNow - firstRunUtc).Days;
        if (daysLeft > 0)
        {
            return (true, $"Version d'essai : {daysLeft} jour{(daysLeft > 1 ? "s" : "")} restant{(daysLeft > 1 ? "s" : "")}.");
        }

        // Délai dépassé
        return (false, "La période d'essai de 30 jours est expirée. Veuillez entrer une clé de licence pour continuer à utiliser GreenBoard.");
    }

    /// <summary>Enregistre la date de premier lancement pour le mode démo.</summary>
    private static void SaveDemoFirstRun()
    {
        var dir = Path.GetDirectoryName(LicencePath)!;
        Directory.CreateDirectory(dir);
        var data = new LicenceData(null, null, DateTimeOffset.UtcNow.DateTime);
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(LicencePath, json);
    }

    /// <summary>Ouvre une boîte de dialogue pour saisir une clé de licence.</summary>
    public static bool PromptForKey(System.Windows.Window owner)
    {
        var dlg = new Views.LicenceKeyWindow { Owner = owner };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.Key))
        {
            var key = dlg.Key.Trim().ToUpperInvariant();
            if (IsValidKey(key))
            {
                Save(key);
                return true;
            }
            System.Windows.MessageBox.Show(owner, "Clé de licence invalide.", "GreenBoard",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
        return false;
    }
}
