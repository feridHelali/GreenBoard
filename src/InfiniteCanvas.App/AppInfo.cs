using System.Reflection;

namespace InfiniteCanvas.App;

/// <summary>Identité de l'application (fenêtre « À propos », aide). La version vient du csproj.</summary>
public static class AppInfo
{
    public const string Name = "GreenBoard";
    public const string Publisher = "Alfa Computers Apps";
    public const string Author = "Ferid HELALI";
    public const string CoAuthor = "Claude Code";
    public const string RepositoryUrl = "https://github.com/feridHelali/GreenBoard";

    private static string Informational =>
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    /// <summary>Version courte, ex. « 1.0.0 ».</summary>
    public static string Version => Informational.Split('+')[0];

    /// <summary>Commit de la compilation (7 caractères), ou null.</summary>
    public static string? Commit
    {
        get
        {
            var parts = Informational.Split('+');
            return parts.Length > 1 && parts[1].Length >= 7 ? parts[1][..7] : null;
        }
    }

    public static string Copyright =>
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? $"© {Publisher}";
}
