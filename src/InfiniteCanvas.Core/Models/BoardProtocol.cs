using System.Text.Json;
using System.Text.Json.Serialization;

namespace InfiniteCanvas.Core.Models;

/// <summary>
/// Protocole local entre l'application GreenBoard (serveur) et le serveur MCP (client).
/// Transport : canal nommé <see cref="BoardProtocol.PipeName"/>, restreint à l'utilisateur courant,
/// une requête JSON par ligne puis une réponse JSON par ligne.
/// </summary>
public static class BoardProtocol
{
    public const string PipeName = "GreenBoard.Board.v1";

    public static class Commands
    {
        /// <summary>État du tableau : arrière-plan, vue courante, éléments.</summary>
        public const string GetBoard = "get_board";
        /// <summary>Ajoute une image PNG/JPEG (<see cref="BoardRequest.ImageBase64"/>).</summary>
        public const string AddImage = "add_image";
        /// <summary>Supprime un élément par son identifiant.</summary>
        public const string RemoveItem = "remove_item";
        /// <summary>Change l'arrière-plan de la page active (<see cref="BoardRequest.Background"/>).</summary>
        public const string SetBackground = "set_background";
        /// <summary>Crée une page (titre dans <see cref="BoardRequest.Label"/>) et l'affiche.</summary>
        public const string AddPage = "add_page";
        /// <summary>Affiche la page <see cref="BoardRequest.PageIndex"/> (0 = première).</summary>
        public const string SelectPage = "select_page";
    }

    /// <summary>Noms d'arrière-plans acceptés (identiques à l'énumération de l'application).</summary>
    public static readonly string[] Backgrounds =
        ["Blanc", "TableauVert", "Quadrillage", "Millimetre", "Seyes", "Pointilles"];

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed record BoardRequest(
    string Command,
    string? ImageBase64 = null,
    double? X = null,
    double? Y = null,
    double? Width = null,
    string? Label = null,
    string? ItemId = null,
    string? Background = null,
    int? PageIndex = null);

public sealed record BoardResponse(
    bool Ok,
    string? Error = null,
    string? ItemId = null,
    BoardInfo? Board = null)
{
    public static BoardResponse Fail(string error) => new(false, error);
}

/// <summary>État de la page active. Coordonnées « monde » (1 unité = 1 px à 100 %).</summary>
public sealed record BoardInfo(
    string Background,
    double Zoom,
    double ViewLeft,
    double ViewTop,
    double ViewWidth,
    double ViewHeight,
    int StrokeCount,
    IReadOnlyList<BoardItemInfo> Items,
    int ActivePage = 0,
    IReadOnlyList<BoardPageInfo>? Pages = null);

public sealed record BoardPageInfo(int Index, string Title, int StrokeCount, int ItemCount);

public sealed record BoardItemInfo(string Id, string? Label, double X, double Y, double Width, double Height);
