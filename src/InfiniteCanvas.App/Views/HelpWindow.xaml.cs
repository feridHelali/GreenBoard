using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace InfiniteCanvas.App.Views;

public enum HelpTab { Features, Assistant, Shortcuts, About }

/// <summary>Aide de GreenBoard : fonctionnalités, raccourcis clavier et « À propos ».</summary>
public partial class HelpWindow : Window
{
    /// <summary>Fonctionnalité : clé d'icône (App.xaml) ou tracé, titre, description.</summary>
    private sealed record Feature(string Icon, string Title, string Text);

    /// <summary>Raccourci : touches (chaque combinaison séparée par « / »), description.</summary>
    private sealed record Shortcut(string Keys, string Text);

    private static readonly Feature[] Features =
    [
        new("IconPen", "Écriture naturelle",
            "Main libre au stylet ou à la souris, sensible à la pression des tablettes graphiques (XP-Pen, Wacom…), " +
            "tracé lissé. Six couleurs, dont le blanc craie pour le tableau vert."),
        new("IconRectangle", "Formes géométriques",
            "Rectangle, ellipse, triangle, ligne et flèche, tracés en glissant. Maj : carré, cercle ou angle multiple de 45°. " +
            "Les formes s'effacent et se sélectionnent comme l'encre."),
        new("IconSelect", "Sélection",
            "Entourez des traits pour les déplacer, les redimensionner, les faire pivoter avec la poignée ronde " +
            "(Maj : par pas de 15°), les recolorer ou les supprimer."),
        new("IconBackground", "Canevas infini et arrière-plans",
            "Zoom de 10 % à 1000 % centré sur le curseur. Six fonds qui suivent le zoom : blanc, tableau vert, " +
            "quadrillage, papier millimétré, cahier Seyès et pointillés."),
        new("IconPage", "Pages en onglets",
            "Plusieurs pages par tableau, chacune avec son titre, son fond, sa vue et son historique. " +
            "Double-clic ou F2 pour renommer, clic droit pour dupliquer ou supprimer."),
        new("IconImage", "Images",
            "Importez des images PNG ou JPEG, glissez-déposez-les ou collez-les (Ctrl V), puis annotez-les à l'encre."),
        new("IconUndo", "Annuler sans limite",
            "Chaque geste s'annule en une fois : traits, formes, images, déplacements, rotations et couleurs."),
        new("IconSave", "Fichiers .gboard",
            "Toutes les pages dans un seul fichier, qui s'ouvre d'un double-clic. Sauvegarde automatique au choix dans les réglages."),
        new("IconExport", "Export",
            "PDF vectoriel de toutes les pages, ou image PNG, JPEG ou SVG de la page active."),
        new("IconShare", "Partage par e-mail",
            "Le tableau est exporté en PDF et joint à un nouveau message de votre messagerie ; vous le relisez avant l'envoi."),
        new("IconSparkle", "Illustrations par l'IA",
            "Le serveur MCP (option de l'installateur) permet à Claude de dessiner des schémas de cours " +
            "(physique, maths, électronique, géographie…) directement sur le tableau ouvert. Voir l'onglet « Assistant IA »."),
        new("IconGear", "Réglages",
            "Roue dentée en bas à gauche : épaisseurs, lissage, pression, fond par défaut, vitesse du zoom, sauvegarde automatique.")
    ];

    private static readonly (string Group, Shortcut[] Items)[] Shortcuts =
    [
        ("Outils",
        [
            new("V", "Sélection"), new("P", "Main libre"), new("E", "Gomme"), new("H", "Déplacer la vue"),
            new("R", "Rectangle"), new("O", "Ellipse"), new("T", "Triangle"), new("L", "Ligne"), new("A", "Flèche"),
            new("Maj", "Pendant un tracé : carré, cercle ou angle de 45°")
        ]),
        ("Fichier",
        [
            new("Ctrl+N", "Nouveau tableau"), new("Ctrl+O", "Ouvrir"), new("Ctrl+S", "Enregistrer"),
            new("Ctrl+Maj+S", "Enregistrer sous"), new("Ctrl+E", "Exporter (PDF, PNG, JPEG, SVG)"),
            new("Ctrl+Maj+E", "Partager par e-mail"), new("Ctrl+I", "Importer une image"), new("Ctrl+V", "Coller une image")
        ]),
        ("Édition",
        [
            new("Ctrl+Z", "Annuler"), new("Ctrl+Y / Ctrl+Maj+Z", "Rétablir"),
            new("Suppr / Retour", "Supprimer la sélection")
        ]),
        ("Pages",
        [
            new("Ctrl+T", "Nouvelle page"), new("Ctrl+Tab / Ctrl+PgSuiv", "Page suivante"),
            new("Ctrl+Maj+Tab / Ctrl+PgPréc", "Page précédente"), new("F2", "Renommer la page"),
            new("Clic milieu", "Sur un onglet : supprimer la page")
        ]),
        ("Vue",
        [
            new("Espace+Glisser", "Déplacer la vue (aussi clic droit ou milieu + glisser)"),
            new("Molette", "Zoomer sur le curseur"), new("Ctrl++ / Ctrl+-", "Zoom avant / arrière"),
            new("Ctrl+0", "Réinitialiser la vue (100 %)")
        ]),
        ("Aide",
        [
            new("F1", "Cette aide"), new("Ctrl+,", "Réglages")
        ])
    ];

    public HelpWindow(HelpTab tab = HelpTab.Features)
    {
        InitializeComponent();
        BuildFeatures();
        BuildShortcuts();
        FillAssistant();
        FillAbout();
        Tabs.SelectedIndex = (int)tab;
    }

    private Geometry IconGeometry(string icon) =>
        TryFindResource(icon) as Geometry ?? Geometry.Parse(icon);

    private void BuildFeatures()
    {
        foreach (var f in Features)
        {
            var row = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());

            var badge = new Border
            {
                Width = 36, Height = 36, CornerRadius = new CornerRadius(9), VerticalAlignment = VerticalAlignment.Top,
                Background = (Brush)FindResource("AccentSoftBrush"),
                Child = new Viewbox
                {
                    Width = 19, Height = 19,
                    Child = new Canvas
                    {
                        Width = 24, Height = 24,
                        Children =
                        {
                            new System.Windows.Shapes.Path
                            {
                                Data = IconGeometry(f.Icon), Stroke = (Brush)FindResource("AccentBrush"), StrokeThickness = 1.9,
                                StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
                            }
                        }
                    }
                }
            };
            row.Children.Add(badge);

            var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
            text.Children.Add(new TextBlock { Text = f.Title, FontWeight = FontWeights.SemiBold, Foreground = Ink(0x1F2937) });
            text.Children.Add(new TextBlock
            {
                Text = f.Text, TextWrapping = TextWrapping.Wrap, Foreground = Ink(0x4B5563), Margin = new Thickness(0, 2, 0, 0)
            });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            FeaturesPanel.Children.Add(row);
        }
    }

    private void BuildShortcuts()
    {
        var keyStyle = (Style)FindResource("KeyCap");
        foreach (var (group, items) in Shortcuts)
        {
            ShortcutsPanel.Children.Add(new TextBlock { Text = group, Style = (Style)FindResource("DialogSection") });
            foreach (var s in items)
            {
                var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
                row.ColumnDefinitions.Add(new ColumnDefinition());

                var keys = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
                var combos = s.Keys.Split(" / ");
                for (int c = 0; c < combos.Length; c++)
                {
                    if (c > 0) keys.Children.Add(Separator("ou"));
                    // « Ctrl++ » : la touche + elle-même
                    string[] parts = combos[c].EndsWith("++") ? [.. combos[c][..^2].Split('+'), "+"] : combos[c].Split('+');
                    for (int p = 0; p < parts.Length; p++)
                    {
                        if (p > 0) keys.Children.Add(Separator("+"));
                        keys.Children.Add(new Border { Style = keyStyle, Child = new TextBlock { Text = parts[p] } });
                    }
                }
                row.Children.Add(keys);

                var desc = new TextBlock
                {
                    Text = s.Text, Foreground = Ink(0x374151), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(desc, 1);
                row.Children.Add(desc);
                ShortcutsPanel.Children.Add(row);
            }
        }
    }

    private static TextBlock Separator(string text) => new()
    {
        Text = text, Foreground = Ink(0x9CA3AF), FontSize = 11, Margin = new Thickness(1, 0, 5, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    private static SolidColorBrush Ink(int rgb) =>
        new(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));

    private void FillAbout()
    {
        Tabs.Tag = $"Version {AppInfo.Version}";
        AboutVersion.Text = $"Version {AppInfo.Version} · {AppInfo.Publisher}";
        DetailVersion.Text = AppInfo.Commit is { } commit ? $"{AppInfo.Version} (compilation {commit})" : AppInfo.Version;
        DetailRuntime.Text = $"{RuntimeInformation.FrameworkDescription} · WPF · Windows {RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}";
        RepoText.Text = AppInfo.RepositoryUrl.Replace("https://", "");
        DetailSettings.Text = AppSettings.DefaultPath;
        DetailSettings.ToolTip = AppSettings.DefaultPath;
        CopyrightText.Text = $"{AppInfo.Copyright}. Conçu par {AppInfo.Author} avec {AppInfo.CoAuthor}.";
    }

    // =====================================================================
    // ASSISTANT IA (serveur MCP)
    // =====================================================================
    /// <summary>Demande type : matière, niveau, texte à copier dans l'assistant.</summary>
    private sealed record PromptExample(string Subject, string Level, string Text);

    private static readonly PromptExample[] Prompts =
    [
        new("Physique", "Lycée",
            "Sur une nouvelle page « Atome », passe le fond en tableau vert et dessine le modèle de Bohr de l'atome de carbone : " +
            "noyau (6 protons, 6 neutrons), deux couches électroniques K et L avec 2 et 4 électrons, légendes en blanc craie."),
        new("Mathématiques", "Lycée",
            "Dessine le cercle trigonométrique de rayon 1 avec les angles remarquables (π/6, π/4, π/3, π/2 et leurs symétriques), " +
            "leurs cosinus et sinus sur les axes, et l'angle θ marqué en couleur."),
        new("Électronique", "BTS / IUT",
            "Dessine un circuit RC série alimenté par un échelon de tension E (générateur, interrupteur K, R, C, flèches de tension uR et uC), " +
            "et à côté la courbe de charge uC(t) avec la tangente à l'origine et τ = RC marqué."),
        new("Informatique", "Lycée (NSI)",
            "Dessine l'organigramme de la recherche dichotomique dans un tableau trié : début, initialisation de gauche/droite, " +
            "boucle tant que, calcul du milieu, trois cas de comparaison, fin. Formes normalisées et flèches nettes."),
        new("Géographie", "Collège",
            "Dessine le cycle de l'eau : océan, évaporation, nuages, condensation, précipitations, ruissellement, infiltration, " +
            "nappe phréatique, avec des flèches colorées et une légende pour chaque étape."),
        new("SVT", "Collège",
            "Dessine une cellule animale en coupe, légendée : membrane plasmique, cytoplasme, noyau, nucléole, mitochondries, " +
            "réticulum endoplasmique et appareil de Golgi. Couleurs douces, traits de légende alignés à droite."),
        new("Chimie", "Lycée",
            "Dessine la molécule d'eau H₂O en représentation de Lewis puis en modèle éclaté (boules), avec l'angle de 104,5°, " +
            "les doublets non liants de l'oxygène et les charges partielles δ+ et δ−."),
        new("Histoire", "Collège",
            "Dessine une frise chronologique de la Révolution française de 1789 à 1799 : prise de la Bastille, abolition des privilèges, " +
            "DDHC, fuite à Varennes, proclamation de la République, Terreur, Directoire, coup d'État de Brumaire."),
        new("Français", "Primaire / Collège",
            "Dessine le schéma narratif en cinq étapes (situation initiale, élément perturbateur, péripéties, élément de résolution, " +
            "situation finale) sous forme d'arc, avec une courte définition sous chaque étape."),
        new("Économie", "Lycée (SES)",
            "Dessine le graphique de l'offre et de la demande : axes prix et quantité, courbe de demande décroissante, courbe d'offre " +
            "croissante, point d'équilibre (P*, Q*) en pointillés, puis un déplacement de l'offre vers la droite en couleur.")
    ];

    /// <summary>Chemin du serveur MCP installé (option de l'installateur), ou celui de la compilation de développement.</summary>
    private static string? FindMcpServer()
    {
        string installed = Path.Combine(AppContext.BaseDirectory, "mcp", "InfiniteCanvas.Mcp.exe");
        if (File.Exists(installed)) return installed;

        // Développement : src\InfiniteCanvas.App\bin\<Config>\net9.0-windows\ → src\InfiniteCanvas.Mcp\bin\<Config>\net9.0\
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? config = dir.Parent?.Name;
        var src = dir.Parent?.Parent?.Parent?.Parent;
        if (src != null && config != null)
        {
            string dev = Path.Combine(src.FullName, "InfiniteCanvas.Mcp", "bin", config, "net9.0", "InfiniteCanvas.Mcp.exe");
            if (File.Exists(dev)) return dev;
        }
        return null;
    }

    private void FillAssistant()
    {
        string? server = FindMcpServer();
        string exe = server ?? Path.Combine(AppContext.BaseDirectory, "mcp", "InfiniteCanvas.Mcp.exe");

        if (server != null)
        {
            McpStatus.Background = Ink(0xECF7EE);
            McpStatus.BorderBrush = Ink(0xB7DFBE);
            McpStatusIcon.Text = "✔";
            McpStatusIcon.Foreground = (Brush)FindResource("AccentBrush");
            McpStatusText.Text = $"Le serveur MCP de GreenBoard est installé :\n{server}";
        }
        else
        {
            McpStatus.Background = Ink(0xFFF7E6);
            McpStatus.BorderBrush = Ink(0xF5D08A);
            McpStatusIcon.Text = "!";
            McpStatusIcon.Foreground = Ink(0xB45309);
            McpStatusText.Text = "Le serveur MCP n'est pas installé. Relancez l'installateur de GreenBoard et choisissez " +
                                 "l'installation « Complète » (ou cochez « Serveur MCP pour assistants IA »). " +
                                 "Les blocs ci-dessous utilisent déjà le bon chemin.";
        }

        // Accents lisibles dans le chemin (le JSON reste valide)
        var indented = new JsonSerializerOptions
        {
            WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        DesktopConfig.Text = JsonSerializer.Serialize(
            new { mcpServers = new { greenboard = new { command = exe } } }, indented);
        CodeCommand.Text = $"claude mcp add greenboard -- \"{exe}\"";
        OpenCodeConfig.Text = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["$schema"] = "https://opencode.ai/config.json",
            ["mcp"] = new { greenboard = new { type = "local", command = new[] { exe }, enabled = true } }
        }, indented);
        McpCommand.Text = exe;

        foreach (var p in Prompts) PromptsPanel.Children.Add(PromptCard(p));
    }

    private Border PromptCard(PromptExample p)
    {
        var header = new DockPanel();
        var copy = new Button
        {
            Content = FindResource("IconCopy"), Style = (Style)FindResource("CopyButton"), Tag = p.Text,
            Margin = new Thickness(0), ToolTip = "Copier la demande"
        };
        copy.Click += Copy_Click;
        DockPanel.SetDock(copy, Dock.Right);
        header.Children.Add(copy);

        var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        chips.Children.Add(new Border
        {
            Background = (Brush)FindResource("AccentBrush"), CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 1, 7, 2),
            Child = new TextBlock { Text = p.Subject, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 12 }
        });
        chips.Children.Add(new TextBlock
        {
            Text = p.Level, Foreground = Ink(0x6B7280), FontSize = 12, Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(chips);

        var body = new StackPanel();
        body.Children.Add(header);
        body.Children.Add(new TextBlock
        {
            Text = $"« {p.Text} »", TextWrapping = TextWrapping.Wrap, Foreground = Ink(0x374151), Margin = new Thickness(0, 6, 0, 0),
            FontStyle = FontStyles.Italic
        });

        return new Border
        {
            Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(12, 10, 10, 12), CornerRadius = new CornerRadius(8),
            Background = Ink(0xF9FAFB), BorderBrush = (Brush)FindResource("DividerBrush"), BorderThickness = new Thickness(1),
            Child = body
        };
    }

    /// <summary>Copie le texte d'un bloc (Tag = TextBox) ou d'une demande type (Tag = texte).</summary>
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        string? text = (sender as FrameworkElement)?.Tag switch
        {
            TextBox box => box.Text,
            string s => s,
            _ => null
        };
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            Clipboard.SetText(text);
            CopiedHint.Visibility = Visibility.Visible;
            if (sender is FrameworkElement el) el.ToolTip = "Copié ✔";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Presse-papiers occupé par une autre application : l'utilisateur peut sélectionner le texte à la main
        }
    }

    private void Link_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppInfo.RepositoryUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Pas de navigateur par défaut : on laisse l'adresse affichée
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
