using System.ComponentModel;
using ModelContextProtocol.Server;

namespace InfiniteCanvas.Mcp;

/// <summary>Modèles de requête proposés à l'enseignant dans son client MCP.</summary>
[McpServerPromptType]
public sealed class TeachingPrompts
{
    [McpServerPrompt(Name = "illustration_cours", Title = "Illustration de cours")]
    [Description("Dessine sur le tableau GreenBoard une illustration pédagogique soignée pour un cours.")]
    public static string IllustrationCours(
        [Description("Matière : physique, mathématiques, électronique, programmation, géographie, SVT, chimie…")] string matiere,
        [Description("Notion à illustrer, ex. « loi d'Ohm », « théorème de Pythagore », « tri à bulles », « cycle de l'eau ».")] string sujet,
        [Description("Niveau des élèves, ex. « 4e », « lycée », « BTS », « licence 1 ».")] string? niveau = null)
    {
        string publicVise = string.IsNullOrWhiteSpace(niveau) ? "" : $" pour des élèves de niveau {niveau}";
        return $"""
            Prépare une illustration pédagogique en {matiere} sur : « {sujet} »{publicVise}.

            Étapes :
            1. Appelle get_board pour connaître l'arrière-plan et la zone visible.
            2. Conçois un SVG clair, exact et beau, en suivant les consignes de conception du serveur GreenBoard.
            3. Vérifie-le avec preview_svg ; corrige tout chevauchement, texte coupé ou erreur scientifique.
            4. Pose-le avec draw_svg (titre court en français).
            5. Résume en 2-3 phrases ce que montre l'illustration et propose une question à poser aux élèves.
            """;
    }
}
