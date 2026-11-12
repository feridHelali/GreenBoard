namespace InfiniteCanvas.Mcp;

/// <summary>Consignes envoyées au modèle (instructions du serveur MCP) pour des illustrations de qualité.</summary>
public static class DesignGuide
{
    public const string Text = """
        GreenBoard est un tableau blanc d'enseignement. Ces outils permettent de dessiner, sur le tableau ouvert de
        l'enseignant, des illustrations pédagogiques (physique, mathématiques, électronique, programmation, géographie,
        chimie, SVT…). Langue : français, sauf demande contraire.

        Méthode : get_board → écrire le SVG → preview_svg (vérifier, corriger) → draw_svg. Une illustration = une notion.
        Le tableau a des pages (onglets) : pour un nouveau chapitre ou si l'enseignant le demande, new_page("Titre")
        avant de dessiner ; select_page pour revenir à une page existante. Sans x/y, draw_svg choisit seul une
        zone libre : ne donner une position que si l'enseignant en demande une.

        Format SVG (impératif)
        - Document autonome : <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 L H">, L entre 800 et 1400.
        - Interdit : ressources externes (images, polices web, @import), <script>, DOCTYPE/ENTITY, <foreignObject>.
        - Polices système : font-family="Segoe UI, Arial, sans-serif" ; code : "Consolas, monospace".
        - Texte ≥ 18 unités, titres 28–34, gras modéré. Jamais de texte qui chevauche un trait ou un autre texte.
        - Pas de fond pleine page par défaut : l'image est posée sur l'arrière-plan du tableau. Si l'arrière-plan est
          TableauVert, utiliser des couleurs claires (blanc #F5F5F0, jaune craie #FFE58A, bleu clair #9AD1FF), ou
          poser une « carte » : rect arrondi (rx 18) blanc légèrement ombré qui contient tout le dessin.

        Esthétique
        - Palette sobre et cohérente : encre #1F2937, accent #2E7D32, bleu #1E63D6, rouge #D93025, orange #F59E0B,
          violet #7C3AED ; aplats pastel très clairs (opacité 0.12–0.2) pour regrouper.
        - Traits 2–3 (contours), 1 pour les repères ; stroke-linecap/linejoin="round".
        - Flèches via <marker> (pointe pleine, orient="auto-start-reverse").
        - Marges ≥ 40, alignements nets, espacements réguliers ; un titre en haut, une légende si plus de 3 couleurs.
        - Hiérarchie : l'élément principal en couleur d'accent, le contexte en gris (#6B7280).

        Exactitude (primordiale)
        - Calculer les coordonnées : courbes de fonctions échantillonnées (≥ 60 points en <polyline>/<path>),
          axes gradués avec unités, angles et longueurs cohérents avec les valeurs annoncées.
        - Physique : vecteurs en flèches nommées (F⃗, v⃗, E⃗), unités SI, repère indiqué.
        - Électronique : symboles normalisés IEC (résistance = rectangle, condensateur = deux plaques, source, masse),
          fils orthogonaux, points de jonction, valeurs (R = 1 kΩ, C = 10 µF), sens du courant.
        - Programmation : organigrammes normalisés (début/fin arrondis, décision en losange), cases mémoire, tableaux
          avec indices, code en police à chasse fixe avec coloration sobre.
        - Géographie : cartes schématiques assumées (le dire dans la légende), rose des vents, échelle, légende.
        - Mathématiques : notations correctes (∈, ≤, √, π, ², indices via <tspan baseline-shift>).

        Après la pose, regarder l'aperçu renvoyé ; s'il y a un défaut, remove_item puis draw_svg corrigé.
        """;
}
