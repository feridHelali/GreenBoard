using System.Diagnostics;
using System.IO;
using System.Net.Mail;
using System.Runtime.InteropServices;
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
/// Partage par e-mail via le logiciel de messagerie de l'utilisateur.
/// GreenBoard n'envoie rien lui-même et ne stocke aucun mot de passe : il prépare un nouveau message
/// (destinataires, objet, texte, PDF joint) que l'utilisateur relit puis envoie.
/// 1. Simple MAPI (<c>MAPISendMailW</c>) : Outlook classique, Thunderbird… — pièce jointe automatique.
/// 2. Sinon (nouvel Outlook, webmail, pas de client) : lien <c>mailto:</c> + dossier du PDF ouvert.
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
    // 1. Simple MAPI : nouveau message du logiciel de messagerie, PDF joint
    // =====================================================================
    public enum MapiOutcome { Shown, Cancelled, Unavailable }

    /// <summary>
    /// Ouvre la fenêtre de rédaction du client de messagerie par défaut (bloquant tant qu'elle est ouverte
    /// pour certains clients : à appeler hors du thread de l'interface).
    /// </summary>
    public static MapiOutcome ComposeWithMailClient(ShareRequest request, string pdfPath, out int mapiCode)
    {
        var allocations = new List<Action>(); // libérations à faire, dans l'ordre
        var recipients = request.Recipients.Select(r => new MapiRecipDescW
        {
            ulRecipClass = MAPI_TO,
            lpszName = r,
            lpszAddress = "SMTP:" + r
        }).ToArray();
        var file = new MapiFileDescW
        {
            nPosition = -1, // pièce jointe en fin de message
            lpszPathName = pdfPath,
            lpszFileName = Path.GetFileName(pdfPath)
        };

        try
        {
            var message = new MapiMessageW
            {
                lpszSubject = request.Subject,
                lpszNoteText = request.Body,
                nRecipCount = recipients.Length,
                lpRecips = ToNativeArray(recipients, allocations),
                nFileCount = 1,
                lpFiles = ToNativeArray([file], allocations)
            };

            mapiCode = MAPISendMailW(IntPtr.Zero, IntPtr.Zero, message, MAPI_LOGON_UI | MAPI_DIALOG, 0);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            mapiCode = -1;
            return MapiOutcome.Unavailable;
        }
        finally
        {
            foreach (var free in allocations) free();
        }

        return mapiCode switch
        {
            SUCCESS_SUCCESS => MapiOutcome.Shown,
            MAPI_USER_ABORT => MapiOutcome.Cancelled,
            _ => MapiOutcome.Unavailable
        };
    }

    /// <summary>
    /// Copie un tableau de structures en mémoire native. La libération (chaînes copiées par le marshaling,
    /// puis le bloc lui-même) est ajoutée à <paramref name="allocations"/>.
    /// </summary>
    private static IntPtr ToNativeArray<T>(T[] items, List<Action> allocations) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        IntPtr block = Marshal.AllocHGlobal(size * items.Length);
        for (int i = 0; i < items.Length; i++)
            Marshal.StructureToPtr(items[i], block + i * size, false);

        allocations.Add(() =>
        {
            for (int i = 0; i < items.Length; i++) Marshal.DestroyStructure<T>(block + i * size);
            Marshal.FreeHGlobal(block);
        });
        return block;
    }

    private const int SUCCESS_SUCCESS = 0;
    private const int MAPI_USER_ABORT = 1;
    private const int MAPI_LOGON_UI = 0x1;
    private const int MAPI_DIALOG = 0x8;
    private const int MAPI_TO = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class MapiMessageW
    {
        public int ulReserved;
        public string? lpszSubject;
        public string? lpszNoteText;
        public string? lpszMessageType;
        public string? lpszDateReceived;
        public string? lpszConversationID;
        public int flFlags;
        public IntPtr lpOriginator;
        public int nRecipCount;
        public IntPtr lpRecips;
        public int nFileCount;
        public IntPtr lpFiles;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MapiRecipDescW
    {
        public int ulReserved;
        public int ulRecipClass;
        public string? lpszName;
        public string? lpszAddress;
        public int ulEIDSize;
        public IntPtr lpEntryID;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MapiFileDescW
    {
        public int ulReserved;
        public int flFlags;
        public int nPosition;
        public string? lpszPathName;
        public string? lpszFileName;
        public IntPtr lpFileType;
    }

    [DllImport("mapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int MAPISendMailW(IntPtr session, IntPtr uiParam, MapiMessageW message, int flags, int reserved);

    // =====================================================================
    // 2. Solution de repli : mailto + dossier du PDF
    // =====================================================================

    /// <summary>Lien mailto (destinataires, objet, texte). Les pièces jointes ne passent pas par mailto.</summary>
    public static string BuildMailto(ShareRequest request)
    {
        string to = string.Join(",", request.Recipients.Select(Uri.EscapeDataString));
        string body = request.Body.Length > 1500 ? request.Body[..1500] + "…" : request.Body;
        return $"mailto:{to}?subject={Uri.EscapeDataString(request.Subject)}&body={Uri.EscapeDataString(body)}";
    }

    public static void OpenMailtoAndRevealFile(ShareRequest request, string pdfPath)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{pdfPath}\"") { UseShellExecute = true });
        Process.Start(new ProcessStartInfo(BuildMailto(request)) { UseShellExecute = true });
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
