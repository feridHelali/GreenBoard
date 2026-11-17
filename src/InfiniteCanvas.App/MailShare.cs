using System.Diagnostics;
using System.IO;
using System.Net.Mail;
using System.Text.Json;

namespace InfiniteCanvas.App;

/// <summary>Ce que l'utilisateur a saisi dans la fenêtre « Partager par e-mail ».</summary>
public sealed record ShareRequest(
    IReadOnlyList<string> Recipients,
    string Subject,
    string Body,
    bool AllPages,
    string FileName);

/// <summary>
/// Partage par e-mail.
/// GreenBoard n'envoie rien lui-même et ne stocke aucun mot de passe : il prépare un nouveau message
/// (destinataires, objet, texte) dans la messagerie par défaut de l'utilisateur via le protocole mailto:,
/// et copie le chemin du PDF dans le presse-papier pour qu'il puisse être joint facilement.
/// Aucun client de messagerie spécifique (Outlook, Thunderbird…) n'est imposé.
/// </summary>
public static class MailShare
{
    // =====================================================================
    // Adresses
    // =====================================================================
    private static readonly char[] Separators = [';', ',', ' ', '\t', '\r', '\n'];

    /// <summary>Découpe la saisie (séparateurs ; , espace ou retour à la ligne), sans doublons.</summary>
    public static (List<string> Valid, List<string> Invalid) ParseRecipients(string? text)
    {
        var valid = new List<string>();
        var invalid = new List<string>();
        foreach (var raw in (text ?? "").Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = raw.Trim();
            var bucket = IsValidAddress(candidate) ? valid : invalid;
            if (!bucket.Contains(candidate, StringComparer.OrdinalIgnoreCase)) bucket.Add(candidate);
        }
        return (valid, invalid);
    }

    public static bool IsValidAddress(string s)
    {
        if (!MailAddress.TryCreate(s, out var address) || address.Address != s) return false;
        int at = s.IndexOf('@');
        return at > 0 && s.IndexOf('.', at) > at + 1 && !s.EndsWith('.');
    }

    /// <summary>Nom de fichier sûr pour Windows (caractères interdits remplacés).</summary>
    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return cleaned.Length == 0 ? "Tableau" : cleaned[..Math.Min(cleaned.Length, 80)];
    }

    // =====================================================================
    // mailto: — ouvre la messagerie par défaut de l'utilisateur sans forcer Outlook
    // =====================================================================

    /// <summary>Lien mailto (destinataires, objet, texte). Les pièces jointes ne passent pas par mailto.</summary>
    public static string BuildMailto(ShareRequest request)
    {
        string to = string.Join(",", request.Recipients.Select(Uri.EscapeDataString));
        string body = request.Body.Length > 1500 ? request.Body[..1500] + "…" : request.Body;
        return $"mailto:{to}?subject={Uri.EscapeDataString(request.Subject)}&body={Uri.EscapeDataString(body)}";
    }

    /// <summary>
    /// Ouvre le message mailto dans la messagerie par défaut, copie le chemin du PDF dans le presse-papier
    /// et affiche le dossier contenant le PDF pour un glisser-déposer facile en pièce jointe.
    /// </summary>
    public static void OpenMailAndRevealFile(ShareRequest request, string pdfPath)
    {
        // Ouvrir le nouveau message dans la messagerie par défaut (mailto:)
        try
        {
            Process.Start(new ProcessStartInfo(BuildMailto(request)) { UseShellExecute = true });
        }
        catch
        {
            // Ignoré : le message d'erreur est géré par l'appelant
        }

        // Copier le chemin du PDF dans le presse-papier (facilite le collage en pièce jointe)
        try
        {
            System.Windows.Clipboard.SetText(pdfPath);
        }
        catch
        {
            // Ignoré : le presse-papier n'est pas essentiel
        }

        // Ouvrir le dossier contenant le PDF pour glisser-déposer facilement
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{pdfPath}\"") { UseShellExecute = true });
        }
        catch
        {
            // Ignoré
        }
    }

    // =====================================================================
    // Mémoire des derniers destinataires (%APPDATA%\GreenBoard\partage.json)
    // =====================================================================
    private static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GreenBoard", "partage.json");

    public static List<string> LoadRecentRecipients()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(SettingsPath)) ?? []
                : [];
        }
        catch
        {
            return []; // réglage illisible : on repart de zéro
        }
    }

    public static void SaveRecentRecipients(IEnumerable<string> recipients)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(recipients.Take(50).ToList()));
        }
        catch
        {
            // non bloquant : simple confort
        }
    }
}
