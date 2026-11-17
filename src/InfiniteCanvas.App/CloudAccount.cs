using System.Text.Json;

namespace InfiniteCanvas.App;

/// <summary>État du compte GreenBoard Cloud (connecté / déconnecté).</summary>
public sealed record CloudAccount
{
    public bool IsConnected { get; init; }
    public string? UserId { get; init; }
    public string? Email { get; init; }
    public string? DisplayName { get; init; }
    /// <summary>Jeton de rafraîchissement chiffré avec DPAPI (base64).</summary>
    public string? EncryptedRefreshToken { get; init; }
    public DateTimeOffset? ConnectedAt { get; init; }

    public static CloudAccount Disconnected() => new() { IsConnected = false };
}

/// <summary>Chiffrement / déchiffrement du jeton de rafraîchissement avec DPAPI.</summary>
/// Le jeton est lié à l'utilisateur courant de la machine (scope Utilisateur).
public static class DpapiHelper
{
    /// <summary>Chiffre une chaîne avec DPAPI et retourne du base64.</summary>
    public static string Encrypt(string plainText)
    {
        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
        var encrypted = System.Security.Cryptography.ProtectedData.Protect(
            plainBytes, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    /// <summary>Déchiffre une chaîne base64 chiffrée avec DPAPI.</summary>
    public static string? Decrypt(string? base64CipherText)
    {
        if (string.IsNullOrEmpty(base64CipherText)) return null;
        try
        {
            var cipherBytes = Convert.FromBase64String(base64CipherText);
            var plainBytes = System.Security.Cryptography.ProtectedData.Unprotect(
                cipherBytes, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return System.Text.Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return null; // jeton invalide (machine réinstallée, autre utilisateur…)
        }
    }

    /// <summary>Sérialise un compte en JSON (sans le jeton en clair).</summary>
    public static string Serialize(CloudAccount account) =>
        JsonSerializer.Serialize(account, new JsonSerializerOptions { WriteIndented = true });

    /// <summary>Désérialise un compte depuis JSON.</summary>
    public static CloudAccount? Deserialize(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<CloudAccount>(json);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Chemin du fichier de compte (hors ligne, utilisateur courant).</summary>
public static class CloudAccountPath
{
    public static string Default =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GreenBoard", "compte.json");
}
