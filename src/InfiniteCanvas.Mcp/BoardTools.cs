using System.ComponentModel;
using System.Text.Json;
using InfiniteCanvas.Core.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace InfiniteCanvas.Mcp;

/// <summary>Outils MCP : l'IA dessine des illustrations pédagogiques (SVG) sur le tableau GreenBoard ouvert.</summary>
[McpServerToolType]
public sealed class BoardTools(BoardClient board)
{
    private const int PreviewSide = 1024;

    [McpServerTool(Name = "get_board", Title = "État du tableau", ReadOnly = true, Idempotent = true)]
    [Description("Renvoie l'état du tableau GreenBoard ouvert : liste des pages (onglets) et page active, puis pour la " +
                 "page active : arrière-plan, zoom, zone visible (coordonnées monde) et éléments déjà posés (id, titre, " +
                 "position, taille). À appeler avant de dessiner pour adapter les couleurs à l'arrière-plan.")]
    public async Task<CallToolResult> GetBoard(CancellationToken ct)
    {
        return await Call(new BoardRequest(BoardProtocol.Commands.GetBoard), ct,
            r => Text($"Tableau :\n{JsonSerializer.Serialize(r.Board, Indented)}"));
    }

    [McpServerTool(Name = "preview_svg", Title = "Aperçu d'une illustration", ReadOnly = true, Idempotent = true)]
    [Description("Rend un SVG en image SANS le poser sur le tableau, pour vérifier visuellement le résultat " +
                 "(lisibilité, chevauchements, exactitude) avant draw_svg. Fonctionne même si GreenBoard est fermé.")]
    public CallToolResult PreviewSvg(
        [Description("Document SVG complet et autonome, avec viewBox.")] string svg)
    {
        try
        {
            var preview = SvgRenderer.Render(svg, oversample: 2f, maxSide: PreviewSide);
            return new CallToolResult
            {
                Content =
                [
                    new TextContentBlock { Text = $"Aperçu {preview.Width:0}×{preview.Height:0} unités. Rien n'a été posé sur le tableau." },
                    ImageContentBlock.FromBytes(preview.Png, "image/png")
                ]
            };
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    [McpServerTool(Name = "draw_svg", Title = "Dessiner une illustration sur le tableau")]
    [Description("Pose une illustration pédagogique (document SVG) sur le tableau GreenBoard. Le SVG est rendu en haute " +
                 "résolution puis placé comme une image que l'enseignant peut déplacer, redimensionner, annoter au stylet, " +
                 "annuler (Ctrl+Z) et exporter. Elle est posée sur la page active ; sans position, GreenBoard choisit " +
                 "un emplacement libre (sans encre ni image) dans la vue de l'enseignant, ou à côté du contenu existant. " +
                 "Renvoie l'identifiant de l'élément et un aperçu de ce qui a été posé.")]
    public async Task<CallToolResult> DrawSvg(
        [Description("Document SVG complet et autonome (viewBox obligatoire ; pas de ressource externe, script ni police web).")] string svg,
        [Description("Titre court de l'illustration (ex. « Circuit RC série »), affiché en info-bulle.")] string? title = null,
        [Description("Optionnel : abscisse monde du coin haut-gauche. Omettre pour centrer dans la vue.")] double? x = null,
        [Description("Optionnel : ordonnée monde du coin haut-gauche. Omettre pour centrer dans la vue.")] double? y = null,
        [Description("Optionnel : largeur sur le tableau en unités monde (défaut : largeur du viewBox, réduite pour tenir dans la vue).")] double? width = null,
        CancellationToken ct = default)
    {
        SvgRenderer.Rendered full, preview;
        try
        {
            full = SvgRenderer.Render(svg);
            preview = SvgRenderer.Render(svg, oversample: 2f, maxSide: PreviewSide);
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }

        // Taille par défaut = taille du viewBox (sans position, l'application la réduit pour tenir dans la vue)
        var request = new BoardRequest(BoardProtocol.Commands.AddImage,
            ImageBase64: Convert.ToBase64String(full.Png),
            X: x, Y: y, Width: width ?? full.Width, Label: title);

        return await Call(request, ct, r =>
        {
            var item = r.Board?.Items.FirstOrDefault(i => i.Id == r.ItemId);
            string where = item == null ? "" : $" à ({item.X:0}, {item.Y:0}), taille {item.Width:0}×{item.Height:0}";
            return new CallToolResult
            {
                Content =
                [
                    new TextContentBlock { Text = $"Illustration posée sur le tableau (id « {r.ItemId} »){where}. " +
                                                  "Pour corriger : remove_item puis draw_svg avec la version améliorée." },
                    ImageContentBlock.FromBytes(preview.Png, "image/png")
                ]
            };
        });
    }

    [McpServerTool(Name = "insert_image", Title = "Insérer une image PNG/JPEG")]
    [Description("Insère sur le tableau une image PNG ou JPEG existante sur le disque de l'utilisateur (chemin absolu).")]
    public async Task<CallToolResult> InsertImage(
        [Description("Chemin absolu du fichier .png, .jpg ou .jpeg.")] string path,
        [Description("Titre court affiché en info-bulle.")] string? title = null,
        [Description("Optionnel : abscisse monde du coin haut-gauche.")] double? x = null,
        [Description("Optionnel : ordonnée monde du coin haut-gauche.")] double? y = null,
        [Description("Optionnel : largeur sur le tableau en unités monde.")] double? width = null,
        CancellationToken ct = default)
    {
        byte[] data;
        try
        {
            if (!Path.IsPathFullyQualified(path)) return Error("Le chemin doit être absolu.");
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is not (".png" or ".jpg" or ".jpeg")) return Error("Seuls les fichiers PNG et JPEG sont acceptés.");
            if (new FileInfo(path).Length > 50 * 1024 * 1024) return Error("Image trop volumineuse (50 Mo maximum).");
            data = await File.ReadAllBytesAsync(path, ct);
            bool png = data.Length > 3 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;
            bool jpg = data.Length > 2 && data[0] == 0xFF && data[1] == 0xD8;
            if (!png && !jpg) return Error("Le fichier n'est pas une image PNG ou JPEG valide.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Error($"Lecture impossible : {ex.Message}");
        }

        var request = new BoardRequest(BoardProtocol.Commands.AddImage, Convert.ToBase64String(data),
            x, y, width, title ?? Path.GetFileNameWithoutExtension(path));
        return await Call(request, ct, r => Text($"Image insérée (id « {r.ItemId} »)."));
    }

    [McpServerTool(Name = "remove_item", Title = "Retirer un élément", Destructive = true)]
    [Description("Retire du tableau une illustration ou image par son identifiant (annulable par l'enseignant avec Ctrl+Z).")]
    public async Task<CallToolResult> RemoveItem(
        [Description("Identifiant renvoyé par draw_svg / insert_image ou listé par get_board.")] string id,
        CancellationToken ct = default)
    {
        return await Call(new BoardRequest(BoardProtocol.Commands.RemoveItem, ItemId: id), ct,
            r => Text($"Élément « {r.ItemId} » retiré."));
    }

    [McpServerTool(Name = "set_background", Title = "Changer l'arrière-plan", Idempotent = true)]
    [Description("Change l'arrière-plan du tableau : Blanc, TableauVert (tableau noir vert, encre claire), Quadrillage, " +
                 "Millimetre (papier millimétré, idéal pour les graphes), Seyes (cahier d'écolier), Pointilles.")]
    public async Task<CallToolResult> SetBackground(
        [Description("Blanc | TableauVert | Quadrillage | Millimetre | Seyes | Pointilles")] string background,
        CancellationToken ct = default)
    {
        return await Call(new BoardRequest(BoardProtocol.Commands.SetBackground, Background: background), ct,
            r => Text($"Arrière-plan : {r.Board?.Background}."));
    }

    [McpServerTool(Name = "new_page", Title = "Nouvelle page")]
    [Description("Crée une nouvelle page (onglet) après la page active, avec le titre donné, et l'affiche. " +
                 "Les dessins suivants y seront posés. Idéal pour structurer un cours : une notion par page.")]
    public async Task<CallToolResult> NewPage(
        [Description("Titre de l'onglet, court (ex. « Loi d'Ohm », « Exercice 2 »). 60 caractères maximum.")] string title,
        CancellationToken ct = default)
    {
        return await Call(new BoardRequest(BoardProtocol.Commands.AddPage, Label: title), ct,
            r => Text($"Page « {title} » créée et affichée (n° {r.Board?.ActivePage}).\n" +
                      $"Pages : {string.Join(" | ", r.Board?.Pages?.Select(p => $"{p.Index}: {p.Title}") ?? [])}"));
    }

    [McpServerTool(Name = "select_page", Title = "Afficher une page", Idempotent = true)]
    [Description("Affiche la page (onglet) d'indice donné — 0 pour la première ; voir get_board pour la liste. " +
                 "Les dessins suivants y seront posés.")]
    public async Task<CallToolResult> SelectPage(
        [Description("Indice de la page, à partir de 0.")] int index,
        CancellationToken ct = default)
    {
        return await Call(new BoardRequest(BoardProtocol.Commands.SelectPage, PageIndex: index), ct,
            r => Text($"Page affichée : {r.Board?.Pages?.ElementAtOrDefault(index)?.Title} (n° {index})."));
    }

    // ---------------------------------------------------------------------
    private static readonly JsonSerializerOptions Indented = new(BoardProtocol.Json) { WriteIndented = true };

    private async Task<CallToolResult> Call(BoardRequest request, CancellationToken ct, Func<BoardResponse, CallToolResult> onSuccess)
    {
        try
        {
            var response = await board.SendAsync(request, ct);
            return response.Ok ? onSuccess(response) : Error(response.Error ?? "Erreur inconnue.");
        }
        catch (BoardUnavailableException ex)
        {
            return Error(ex.Message);
        }
        catch (IOException ex)
        {
            return Error($"Communication avec GreenBoard impossible : {ex.Message}");
        }
    }

    private static CallToolResult Text(string text) => new() { Content = [new TextContentBlock { Text = text }] };

    private static CallToolResult Error(string message) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = message }]
    };
}
