using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Ink;
using InfiniteCanvas.Core.Models;
using InfiniteCanvas.App.Views;
using Microsoft.Win32;

namespace InfiniteCanvas.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    // --- Outils disponibles dans la barre d'outils ---
    private enum Tool { Select, Pen, Eraser, Pan, Rectangle, Ellipse, Triangle, Line, Arrow, Text }

    /// <summary>Image posée sur le tableau : stockée dans le Tag de l'élément <see cref="Image"/>.</summary>
    private sealed record BoardItemTag(string Id, byte[] Data, string? Label);

    /// <summary>Texte posé sur le tableau : stocké dans le Tag de l'élément (Border contenant les contours des glyphes).</summary>
    private sealed record BoardTextTag(string Id, string Text, double FontSize, Color Color);

    private const double MinScale = 0.1;   // 10 %
    private const double MaxScale = 10.0;  // 1000 % (cible du cahier des charges)
    private const string AppName = "GreenBoard";

    /// <summary>Propriété ISF marquant un trait comme forme géométrique (valeur = nom de l'outil).</summary>
    private static readonly Guid ShapeKindProperty = new("6f3b6a43-6c2e-4b8e-9a0c-2f1d8d6b5a11");

    /// <summary>
    /// Une page du classeur. Une seule InkCanvas sert toutes les pages : à l'activation, on y place les traits
    /// et les images de la page ; à la désactivation, on mémorise ses éléments, son arrière-plan et sa vue.
    /// Chaque page a son propre historique Annuler/Rétablir.
    /// </summary>
    private sealed class BoardPage(string id, string title, StrokeCollection strokes, BoardHistory history)
    {
        public string Id { get; } = id;
        public string Title { get; set; } = title;
        public StrokeCollection Strokes { get; } = strokes;
        public BoardHistory History { get; } = history;
        public List<UIElement> Elements { get; set; } = [];
        public BoardBackground Background { get; set; }
        public double Scale { get; set; } = 1;
        public double TranslateX { get; set; }
        public double TranslateY { get; set; }
        /// <summary>Faux tant que la page n'a jamais été affichée : on centre alors la vue.</summary>
        public bool HasView { get; set; }
    }

    private Tool _tool = Tool.Pen;
    private readonly Dictionary<Tool, RadioButton> _toolButtons = new();
    private BoardPipeServer? _pipeServer;

    // Cloud (GreenBoard Cloud — optionnel, hors-ligne par défaut)
    private readonly CloudClient _cloudClient = CloudClient.Offline();
    private CloudAccount _cloudAccount = CloudAccount.Disconnected();
    private string? _cloudAccessToken;

    // Pages (onglets)
    private readonly List<BoardPage> _pages = [];
    private BoardPage? _page;
    private BoardPage Page => _page!;
    private BoardHistory _history => Page.History;
    private readonly Dictionary<BoardPage, (TextBlock Title, TextBox Editor)> _tabParts = new();

    // Fichier courant
    private string? _currentFile;
    private bool _dirty;

    // Déplacement de la vue (pan)
    private Point _panStart;
    private bool _isPanning;
    private bool _spaceHeld;

    // Forme en cours de tracé (c'est un trait d'encre comme les autres)
    private Stroke? _shapeStroke;
    private Tool _shapeTool;
    private Point _shapeStart;

    // Texte en cours de saisie (TextBox posée sur la feuille) et texte existant en cours de modification
    private TextBox? _textEditor;
    private Border? _editingText;
    private Color _textEditorColor;

    // Instantané de la sélection avant déplacement / redimensionnement (pour Annuler)
    private Dictionary<Stroke, StylusPointCollection>? _selectionPoints;
    private Dictionary<FrameworkElement, Rect>? _selectionBounds;

    // Arrière-plan
    private BoardBackground _background = BoardBackground.Blanc;
    private Brush _backgroundBrush = Brushes.White;
    private double _backgroundBuiltScale = double.NaN;
    private readonly Dictionary<BoardBackground, Border> _backgroundPreviews = new();

    // Réglages de l'utilisateur (roue dentée) et sauvegarde automatique
    private AppSettings _settings = AppSettings.Load();
    private readonly System.Windows.Threading.DispatcherTimer _autoSaveTimer = new();

    public MainWindow()
    {
        InitializeComponent();
        InitInkCanvas();
        _autoSaveTimer.Tick += (_, _) => AutoSave();
        ApplySettings(_settings);

        var first = CreatePage(BoardFile.NewId(), "Page 1", new StrokeCollection(), _settings.DefaultBackground);
        _pages.Add(first);
        ActivatePage(first, storeCurrent: false);

        _toolButtons[Tool.Select] = BtnSelect;
        _toolButtons[Tool.Pen] = BtnPen;
        _toolButtons[Tool.Eraser] = BtnEraser;
        _toolButtons[Tool.Pan] = BtnPan;
        _toolButtons[Tool.Rectangle] = BtnRectangle;
        _toolButtons[Tool.Ellipse] = BtnEllipse;
        _toolButtons[Tool.Triangle] = BtnTriangle;
        _toolButtons[Tool.Line] = BtnLine;
        _toolButtons[Tool.Arrow] = BtnArrow;
        _toolButtons[Tool.Text] = BtnText;

        BuildBackgroundChoices();
        BtnPen.IsChecked = true;
        UpdateTitle();

        // Charger le compte cloud (hors-ligne par défaut, fichier compte.json s'il existe)
        LoadCloudAccount();
        UpdateCloudButton();

        Loaded += (_, _) =>
        {
            ResetView();
            CanvasContainer.Focus();

            // Double-clic sur un fichier .gboard (association installée) : GreenBoard.exe "chemin\tableau.gboard"
            OpenFromCommandLine(Environment.GetCommandLineArgs().Skip(1));

            // Canal local pour le serveur MCP (illustrations générées par l'IA)
            _pipeServer = new BoardPipeServer(Dispatcher, HandleBoardRequest);
            _pipeServer.Start();
        };
        Closed += (_, _) => _pipeServer?.Dispose();
    }

    private void InitInkCanvas()
    {
        // Configuration initiale du stylet pour capter la pression nativement
        MyInkCanvas.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = (Color)ColorConverter.ConvertFromString("#1F2328"),
            Width = 3,
            Height = 3,
            FitToCurve = true, // Force le lissage vectoriel en courbes de Bézier natif WPF
            IgnorePressure = false // Important pour votre XP-Pen !
        };
    }

    private Color CurrentColor => MyInkCanvas.DefaultDrawingAttributes.Color;

    // =====================================================================
    // RÉGLAGES, AIDE, SAUVEGARDE AUTOMATIQUE
    // =====================================================================
    /// <summary>Applique des réglages au tableau (aussi appelé en direct pendant l'édition des réglages).</summary>
    private void ApplySettings(AppSettings s)
    {
        _settings = s;
        // Nouvelle instance : les traits existants gardent leurs attributs
        var da = MyInkCanvas.DefaultDrawingAttributes.Clone();
        da.Width = da.Height = s.PenThickness;
        da.FitToCurve = s.PenSmoothing;
        da.IgnorePressure = !s.PenPressure;
        MyInkCanvas.DefaultDrawingAttributes = da;

        _autoSaveTimer.Stop();
        if (s.AutoSaveMinutes > 0)
        {
            _autoSaveTimer.Interval = TimeSpan.FromMinutes(s.AutoSaveMinutes);
            _autoSaveTimer.Start();
        }
    }

    // Fenêtre affichée (plein écran) : l'écran de démarrage s'efface
    private void Window_ContentRendered(object? sender, EventArgs e) => (Application.Current as App)?.HideSplash();

    private void Settings_Click(object sender, RoutedEventArgs e) => ShowSettings();
    private void Help_Click(object sender, RoutedEventArgs e) => ShowHelp(Views.HelpTab.Features);

    private void ShowSettings()
    {
        ReleaseSpace();
        var dialog = new Views.SettingsWindow(_settings, ApplySettings, _cloudClient, _cloudAccount) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            ApplySettings(dialog.Result);
            if (!_settings.Save())
                MessageBox.Show(this, "Les réglages sont appliqués mais n'ont pas pu être enregistrés sur le disque.", AppName,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        CanvasContainer.Focus();
    }

    private void ShowHelp(Views.HelpTab tab)
    {
        ReleaseSpace();
        new Views.HelpWindow(tab) { Owner = this }.ShowDialog();
        CanvasContainer.Focus();
    }

    // =====================================================================
    // CLOUD GREENBOARD (compte optionnel, hors-ligne par défaut)
    // =====================================================================

    private void LoadCloudAccount()
    {
        try
        {
            var path = CloudAccountPath.Default;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var saved = DpapiHelper.Deserialize(json);
                if (saved != null && saved.IsConnected && saved.EncryptedRefreshToken != null)
                {
                    // Restaurer le compte (jeton déchiffré avec DPAPI)
                    var refreshToken = DpapiHelper.Decrypt(saved.EncryptedRefreshToken);
                    if (refreshToken != null)
                    {
                        _cloudAccount = saved with { };
                        _cloudAccessToken = null; // sera obtenu par refresh au premier usage
                        // Tenter un refresh silencieux pour obtenir un access token
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var result = await _cloudClient.RefreshAsync("", refreshToken, default);
                                if (result != null && !result.IsOffline && result.AccessToken != null)
                                {
                                    _cloudAccessToken = result.AccessToken;
                                    Dispatcher.Invoke(() => { UpdateCloudButton(); UpdateTitle(); });
                                }
                            }
                            catch { /* serveur indisponible : reste hors-ligne */ }
                        });
                    }
                }
            }
        }
        catch { /* compte ignoré si illisible */ }
    }

    private void UpdateCloudButton()
    {
        BtnCloud.IsChecked = _cloudAccount.IsConnected;
        BtnCloud.ToolTip = _cloudAccount.IsConnected
            ? $"Connecté ({_cloudAccount.Email}) — Fermer la session"
            : "Espace cloud GreenBoard — Se connecter";
    }

    private async void Cloud_Click(object sender, RoutedEventArgs e)
    {
        ReleaseSpace();

        if (!_cloudAccount.IsConnected)
        {
            var dialog = new Views.AccountWindow(_cloudClient, _cloudAccount) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                // Recharger le compte depuis le fichier (sauvegardé par AccountWindow)
                LoadCloudAccount();
                UpdateCloudButton();
                UpdateTitle();
            }
            return;
        }

        // Déjà connecté : afficher les tableaux en ligne
        var boardsWindow = new Views.OnlineBoardsWindow(_cloudClient, _cloudAccount, (data, title) =>
        {
            // Ouvrir le document .gboard téléchargé
            try
            {
                var doc = BoardFile.LoadFromBytes(data);
                LoadDocument(doc, null);
                _currentFile = null; // fichier cloud : pas de chemin local
                _dirty = false;
                UpdateTitle();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Impossible d'ouvrir le tableau :\n{ex.Message}", AppName,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        });
        boardsWindow.SetAccessToken(_cloudAccessToken ?? "");
        var result = boardsWindow.ShowDialog();
        if (result == false)
        {
            // Déconnexion demandée
            _cloudAccount = CloudAccount.Disconnected();
            _cloudAccessToken = null;
            try { File.Delete(CloudAccountPath.Default); } catch { }
            UpdateCloudButton();
            UpdateTitle();
        }
    }

    private async Task SaveToCloudAsync()
    {
        if (!_cloudAccount.IsConnected)
        {
            MessageBox.Show(this, "Veuillez vous connecter à votre compte GreenBoard Cloud d'abord.",
                AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var pages = BuildExportPages();
        int active = _pages.IndexOf(Page);
        if (pages.All(p => p.IsEmpty))
        {
            MessageBox.Show(this, "Le tableau est vide : rien à enregistrer.", AppName,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string boardName = _currentFile == null ? Page.Title : Path.GetFileNameWithoutExtension(_currentFile);
        var nameDialog = new NameInputDialog(boardName) { Owner = this };
        if (nameDialog.ShowDialog() != true || string.IsNullOrWhiteSpace(nameDialog.DialogName)) return;
        boardName = nameDialog.DialogName;

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var docBytes = BoardFile.SerializeToBytes(Snapshot());
            var result = await _cloudClient.UploadDocumentAsync(_cloudAccessToken ?? "", boardName, docBytes, pages.Count, default);
            if (result != null)
            {
                MessageBox.Show(this, $"Tableau « {boardName} » enregistré dans votre espace cloud.",
                    AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(this, "Échec de l'enregistrement dans le cloud (serveur indisponible).",
                    AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Erreur : {ex.Message}", AppName,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    /// <summary>Enregistre en silence un tableau qui a déjà un fichier, hors geste en cours.</summary>
    private void AutoSave()
    {
        if (!_dirty || _currentFile == null || !IsLoaded) return;
        if (Mouse.LeftButton == MouseButtonState.Pressed || _shapeStroke != null || Keyboard.FocusedElement is TextBox) return;
        if (OwnedWindows.Count > 0) return; // une fenêtre de dialogue est ouverte

        try
        {
            BoardFile.Save(_currentFile, Snapshot());
            _dirty = false;
            UpdateTitle();
        }
        catch (Exception)
        {
            // Échec silencieux : le titre garde « • », la sauvegarde manuelle signalera l'erreur
        }
    }

    // =====================================================================
    // GESTION DES OUTILS
    // =====================================================================
    private void Tool_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && Enum.TryParse(tag, out Tool tool))
        {
            if (tool != Tool.Text) CommitTextEdit();
            _tool = tool;
            ApplyTool();
        }
    }

    private void SelectTool(Tool tool) => _toolButtons[tool].IsChecked = true;

    private static bool IsShapeTool(Tool tool) =>
        tool is Tool.Rectangle or Tool.Ellipse or Tool.Triangle or Tool.Line or Tool.Arrow;

    /// <summary>Applique l'outil courant à l'InkCanvas (mode d'édition + curseur).</summary>
    private void ApplyTool()
    {
        if (MyInkCanvas == null) return;
        ApplyToolCore();
        UpdateRotateHandle();
    }

    private void ApplyToolCore()
    {

        // Espace maintenu ou outil « Déplacer » : aucune encre, curseur de déplacement
        if (_spaceHeld || _tool == Tool.Pan)
        {
            MyInkCanvas.EditingMode = InkCanvasEditingMode.None;
            MyInkCanvas.UseCustomCursor = true;
            MyInkCanvas.Cursor = Cursors.ScrollAll;
            CanvasContainer.Cursor = Cursors.ScrollAll;
            return;
        }

        CanvasContainer.Cursor = null;
        switch (_tool)
        {
            case Tool.Select:
                MyInkCanvas.EditingMode = InkCanvasEditingMode.Select;
                MyInkCanvas.UseCustomCursor = false;
                break;
            case Tool.Pen:
                MyInkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                MyInkCanvas.UseCustomCursor = false;
                break;
            case Tool.Eraser:
                // Gomme par trait : efface le trait complet au toucher (formes comprises, ce sont des traits)
                MyInkCanvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
                MyInkCanvas.UseCustomCursor = false;
                break;
            case Tool.Text:
                MyInkCanvas.EditingMode = InkCanvasEditingMode.None;
                MyInkCanvas.UseCustomCursor = true;
                MyInkCanvas.Cursor = Cursors.IBeam;
                break;
            default: // Formes
                MyInkCanvas.EditingMode = InkCanvasEditingMode.None;
                MyInkCanvas.UseCustomCursor = true;
                MyInkCanvas.Cursor = Cursors.Cross;
                break;
        }
    }

    private void Color_Checked(object sender, RoutedEventArgs e)
    {
        if (MyInkCanvas == null || sender is not RadioButton { Tag: string hex }) return;

        var color = (Color)ColorConverter.ConvertFromString(hex);
        MyInkCanvas.DefaultDrawingAttributes.Color = color;

        // Saisie de texte en cours : la couleur s'applique au texte (les pastilles ne prennent pas le focus)
        if (_textEditor != null)
        {
            _textEditorColor = color;
            _textEditor.Foreground = _textEditor.CaretBrush = new SolidColorBrush(color);
            return;
        }

        // Outil Sélection avec des traits sélectionnés : on les recolore (annulable)
        if (_tool == Tool.Select)
        {
            var selected = MyInkCanvas.GetSelectedStrokes();
            if (selected.Count > 0)
            {
                var changes = new List<(Stroke, DrawingAttributes, DrawingAttributes)>();
                foreach (var stroke in selected)
                {
                    var before = stroke.DrawingAttributes.Clone();
                    var after = stroke.DrawingAttributes.Clone();
                    after.Color = color;
                    stroke.DrawingAttributes = after;
                    changes.Add((stroke, before, after.Clone()));
                }
                _history.RecordAttributes(changes);
                return;
            }
        }

        // Repasser automatiquement en mode écriture si on choisit une couleur (sauf outil de forme)
        if (_tool is Tool.Eraser or Tool.Pan or Tool.Select)
            SelectTool(Tool.Pen);
    }

    // =====================================================================
    // RACCOURCIS CLAVIER + ESPACE MAINTENU POUR DÉPLACER
    // =====================================================================
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Saisie en cours (titre d'onglet) : aucune touche ne doit déclencher un raccourci
        if (Keyboard.FocusedElement is TextBox) return;

        if (e.Key == Key.Space)
        {
            // Empêche aussi l'Espace d'« appuyer » sur un bouton de la barre d'outils
            e.Handled = true;
            if (!_spaceHeld && _shapeStroke == null)
            {
                _spaceHeld = true;
                ApplyTool();
            }
            return;
        }

        var mods = Keyboard.Modifiers;
        if (mods == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            switch (e.Key)
            {
                case Key.Z: _history.Redo(); e.Handled = true; break; // Ctrl+Maj+Z = Rétablir
                case Key.S: SaveAs(); e.Handled = true; break;
                case Key.E: _ = ShareBoardAsync(); e.Handled = true; break;
                case Key.U: _ = SaveToCloudAsync(); e.Handled = true; break;
                case Key.Tab: CyclePage(-1); e.Handled = true; break;
            }
            return;
        }

        if (mods == ModifierKeys.Control)
        {
            e.Handled = true;
            switch (e.Key)
            {
                case Key.Z: _history.Undo(); break;
                case Key.Y: _history.Redo(); break;
                case Key.S: Save(); break;
                case Key.O: OpenBoard(); break;
                case Key.N: NewBoard(); break;
                case Key.E: ExportBoard(); break;
                case Key.I: ImportImages(); break;
                case Key.V: PasteFromClipboard(); break;
                case Key.T: AddPage(); break;
                case Key.Tab: case Key.PageDown: CyclePage(+1); break;
                case Key.PageUp: CyclePage(-1); break;
                case Key.D0: case Key.NumPad0: ResetView(); break;
                case Key.OemComma: ShowSettings(); break;
                case Key.OemPlus: case Key.Add: ZoomAtCenter(1.2); break;
                case Key.OemMinus: case Key.Subtract: ZoomAtCenter(1 / 1.2); break;
                default: e.Handled = false; break;
            }
            return;
        }

        if (mods != ModifierKeys.None) return;

        if (e.Key is Key.Delete or Key.Back)
        {
            DeleteSelection();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F1)
        {
            ShowHelp(Views.HelpTab.Features);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F2)
        {
            BeginRename(Page);
            e.Handled = true;
            return;
        }

        Tool? tool = e.Key switch
        {
            Key.V => Tool.Select,
            Key.P => Tool.Pen,
            Key.E => Tool.Eraser,
            Key.H => Tool.Pan,
            Key.R => Tool.Rectangle,
            Key.O => Tool.Ellipse,
            Key.T => Tool.Triangle,
            Key.L => Tool.Line,
            Key.A => Tool.Arrow,
            Key.X => Tool.Text,
            _ => null
        };
        if (tool is { } t)
        {
            SelectTool(t);
            e.Handled = true;
        }
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || Keyboard.FocusedElement is TextBox) return;
        e.Handled = true;
        ReleaseSpace();
    }

    private void Window_Deactivated(object? sender, EventArgs e) => ReleaseSpace();

    private void ReleaseSpace()
    {
        if (!_spaceHeld) return;
        _spaceHeld = false;
        // Si un déplacement est en cours, on restaure l'outil au relâchement du clic
        if (!_isPanning) ApplyTool();
    }

    // Un geste (appui → relâchement) = une seule étape d'annulation (ex. : gomme passée sur 10 traits)
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) _history.BeginGroup();
    }

    private void Window_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) _history.EndGroup();
    }

    // =====================================================================
    // ANNULER / RÉTABLIR / SUPPRIMER
    // =====================================================================
    private void Undo_Click(object sender, RoutedEventArgs e) => _history.Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => _history.Redo();

    private void UpdateUndoButtons()
    {
        BtnUndo.IsEnabled = _history.CanUndo;
        BtnRedo.IsEnabled = _history.CanRedo;
    }

    private void DeleteSelection()
    {
        var strokes = MyInkCanvas.GetSelectedStrokes();
        var elements = MyInkCanvas.GetSelectedElements().ToList();
        if (strokes.Count == 0 && elements.Count == 0) return;

        _history.BeginGroup();
        MyInkCanvas.Select(new StrokeCollection());
        MyInkCanvas.Strokes.Remove(strokes);
        foreach (var element in elements)
        {
            MyInkCanvas.Children.Remove(element);
            _history.RecordElementRemoved(element);
        }
        _history.EndGroup();
    }

    private void InkCanvas_SelectionChanging(object sender, InkCanvasSelectionEditingEventArgs e)
    {
        _selectionPoints = MyInkCanvas.GetSelectedStrokes().ToDictionary(s => s, s => s.StylusPoints.Clone());
        _selectionBounds = MyInkCanvas.GetSelectedElements().OfType<FrameworkElement>()
            .ToDictionary(el => el, BoardHistory.BoundsOf);
    }

    private void InkCanvas_SelectionChangedGeometry(object sender, EventArgs e)
    {
        _history.BeginGroup();
        if (_selectionPoints != null)
            _history.RecordPoints(_selectionPoints.Select(p => (p.Key, p.Value, p.Key.StylusPoints.Clone())));
        if (_selectionBounds != null)
            _history.RecordBounds(_selectionBounds.Select(b => (b.Key, b.Value, BoardHistory.BoundsOf(b.Key))));
        _history.EndGroup();
        _selectionPoints = null;
        _selectionBounds = null;
        UpdateRotateHandle();
    }

    private void InkCanvas_SelectionChanged(object? sender, EventArgs e) => UpdateRotateHandle();

    // =====================================================================
    // ROTATION DE LA SÉLECTION (poignée au-dessus du cadre de sélection)
    // =====================================================================
    private const double RotateHandleOffset = 34; // distance écran entre le cadre et la poignée
    private bool _rotating;
    private Point _rotateCenter;
    private double _rotateStartAngle;
    private double _rotateAngle;
    private Dictionary<Stroke, StylusPointCollection>? _rotateSnapshot;

    /// <summary>Affiche la poignée au-dessus du centre haut de la sélection (outil Sélection, traits sélectionnés).</summary>
    private void UpdateRotateHandle()
    {
        if (RotateHandle == null || _rotating) return;

        bool show = _tool == Tool.Select && !_spaceHeld && MyInkCanvas.EditingMode == InkCanvasEditingMode.Select
                    && MyInkCanvas.GetSelectedStrokes().Count > 0;
        RotateHandle.Visibility = RotateStem.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        AngleBadge.Visibility = Visibility.Collapsed;
        if (!show) return;

        Rect b = MyInkCanvas.GetSelectionBounds();
        Point top = MyInkCanvas.TranslatePoint(new Point(b.X + b.Width / 2, b.Y), CanvasContainer);
        var handle = new Point(top.X, top.Y - RotateHandleOffset);
        PlaceHandle(handle);
        RotateStem.X1 = top.X;
        RotateStem.Y1 = top.Y;
        RotateStem.X2 = handle.X;
        RotateStem.Y2 = handle.Y + RotateHandle.Height / 2;
    }

    private void PlaceHandle(Point center)
    {
        Canvas.SetLeft(RotateHandle, center.X - RotateHandle.Width / 2);
        Canvas.SetTop(RotateHandle, center.Y - RotateHandle.Height / 2);
    }

    private double AngleFromCenter(Point p) =>
        Math.Atan2(p.Y - _rotateCenter.Y, p.X - _rotateCenter.X) * 180 / Math.PI;

    private void RotateHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var strokes = MyInkCanvas.GetSelectedStrokes();
        if (strokes.Count == 0) return;

        Rect b = MyInkCanvas.GetSelectionBounds();
        _rotateCenter = new Point(b.X + b.Width / 2, b.Y + b.Height / 2);
        _rotateSnapshot = strokes.ToDictionary(s => s, s => s.StylusPoints.Clone());
        _rotateStartAngle = AngleFromCenter(e.GetPosition(MyInkCanvas));
        _rotateAngle = 0;
        _rotating = true;

        RotateStem.Visibility = Visibility.Collapsed;
        RotateHandle.CaptureMouse();
        e.Handled = true;
    }

    private void RotateHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_rotating || _rotateSnapshot == null) return;

        double angle = AngleFromCenter(e.GetPosition(MyInkCanvas)) - _rotateStartAngle;
        angle = (angle % 360 + 540) % 360 - 180; // ramené dans ]-180°, 180°]
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) angle = Math.Round(angle / 15) * 15;
        _rotateAngle = angle;

        // Rotation recalculée depuis l'état initial : aucune erreur cumulée pendant le glisser
        var m = Matrix.Identity;
        m.RotateAt(angle, _rotateCenter.X, _rotateCenter.Y);
        foreach (var (stroke, original) in _rotateSnapshot)
        {
            stroke.StylusPoints = original.Clone();
            stroke.Transform(m, applyToStylusTip: false);
        }

        // La poignée suit la souris ; l'angle s'affiche à côté
        Point screen = e.GetPosition(CanvasContainer);
        PlaceHandle(screen);
        AngleText.Text = $"{angle:0}°";
        AngleBadge.Visibility = Visibility.Visible;
        Canvas.SetLeft(AngleBadge, screen.X + 20);
        Canvas.SetTop(AngleBadge, screen.Y - 30);
        e.Handled = true;
    }

    private void RotateHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_rotating) return;
        RotateHandle.ReleaseMouseCapture(); // → LostMouseCapture → EndRotate
        e.Handled = true;
    }

    private void RotateHandle_LostMouseCapture(object sender, MouseEventArgs e) => EndRotate();

    private void EndRotate()
    {
        if (!_rotating || _rotateSnapshot == null) return;
        _rotating = false;

        var strokes = new StrokeCollection(_rotateSnapshot.Keys);
        if (Math.Abs(_rotateAngle) > 0.01)
        {
            _history.EndGroup();
            _history.RecordPoints(_rotateSnapshot.Select(kv => (kv.Key, kv.Value, kv.Key.StylusPoints.Clone())));
        }
        _rotateSnapshot = null;

        // Resélectionne pour recalculer le cadre (et ses poignées de redimensionnement) autour du résultat
        MyInkCanvas.Select(strokes, MyInkCanvas.GetSelectedElements());
        UpdateRotateHandle();
    }

    // =====================================================================
    // ZOOM INTERACTIF (écran = monde × échelle + translation)
    // =====================================================================
    private double Scale => ControlScale.ScaleX;

    private void CanvasContainer_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double zoomFactor = e.Delta > 0 ? _settings.WheelZoomStep : 1 / _settings.WheelZoomStep;
        ZoomAt(e.GetPosition(CanvasContainer), zoomFactor);
        e.Handled = true;
    }

    /// <summary>Zoome en gardant fixe le point écran <paramref name="anchor"/>.</summary>
    private void ZoomAt(Point anchor, double factor)
    {
        double newScale = Math.Clamp(Scale * factor, MinScale, MaxScale);
        double f = newScale / Scale;
        if (Math.Abs(f - 1) < 1e-9) return;

        ControlScale.ScaleX = ControlScale.ScaleY = newScale;
        ControlTranslate.X = anchor.X - (anchor.X - ControlTranslate.X) * f;
        ControlTranslate.Y = anchor.Y - (anchor.Y - ControlTranslate.Y) * f;
        OnViewChanged();
    }

    private void ZoomAtCenter(double factor) =>
        ZoomAt(new Point(CanvasContainer.ActualWidth / 2, CanvasContainer.ActualHeight / 2), factor);

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomAtCenter(1.2);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomAtCenter(1 / 1.2);
    private void ResetView_Click(object sender, RoutedEventArgs e) => ResetView();

    /// <summary>Zoom 100 % centré au milieu de la feuille.</summary>
    private void ResetView()
    {
        ControlScale.ScaleX = ControlScale.ScaleY = 1;
        ControlTranslate.X = CanvasContainer.ActualWidth / 2 - MainCanvas.Width / 2;
        ControlTranslate.Y = CanvasContainer.ActualHeight / 2 - MainCanvas.Height / 2;
        OnViewChanged();
    }

    private void CanvasContainer_SizeChanged(object sender, SizeChangedEventArgs e) => OnViewChanged();

    /// <summary>Après chaque zoom/déplacement : borne la vue, met à jour le motif et le % affiché.</summary>
    private void OnViewChanged()
    {
        ClampView();
        UpdateBackgroundBrush();
        ZoomText.Text = $"{Math.Round(Scale * 100)} %";
        UpdateRotateHandle(); // la poignée est en coordonnées écran : elle suit zoom et déplacement
    }

    /// <summary>Empêche de sortir de la feuille (20 000 × 20 000) et de s'y perdre.</summary>
    private void ClampView()
    {
        double viewW = CanvasContainer.ActualWidth, viewH = CanvasContainer.ActualHeight;
        double worldW = MainCanvas.Width * Scale, worldH = MainCanvas.Height * Scale;

        ControlTranslate.X = worldW > viewW
            ? Math.Clamp(ControlTranslate.X, viewW - worldW, 0)
            : (viewW - worldW) / 2;
        ControlTranslate.Y = worldH > viewH
            ? Math.Clamp(ControlTranslate.Y, viewH - worldH, 0)
            : (viewH - worldH) / 2;
    }

    /// <summary>Zone du tableau actuellement visible, en coordonnées monde.</summary>
    private Rect VisibleWorldRect() => new(
        -ControlTranslate.X / Scale,
        -ControlTranslate.Y / Scale,
        CanvasContainer.ActualWidth / Scale,
        CanvasContainer.ActualHeight / Scale);

    // =====================================================================
    // DÉPLACEMENT (PAN) : Espace + clic gauche, outil « Déplacer », clic droit ou central
    // =====================================================================
    private void CanvasContainer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Boutons flottants (réglages, aide) : clic normal, jamais de déplacement de la vue
        if (e.OriginalSource is Visual source && CornerButtons.IsAncestorOf(source)) return;
        // Clic dans le texte en cours de saisie : il garde le focus (placement du curseur, sélection)
        if (_textEditor != null && e.OriginalSource is Visual inEditor && _textEditor.IsAncestorOf(inEditor)) return;

        CanvasContainer.Focus(); // garde le clavier (Espace, raccourcis) sur la zone de dessin

        bool leftPans = e.ChangedButton == MouseButton.Left && (_spaceHeld || _tool == Tool.Pan);
        bool otherPans = e.ChangedButton is MouseButton.Right or MouseButton.Middle;
        if (!leftPans && !otherPans) return;

        _isPanning = true;
        _panStart = e.GetPosition(CanvasContainer);
        CanvasContainer.CaptureMouse();
        CanvasContainer.Cursor = Cursors.ScrollAll;
        e.Handled = true; // l'InkCanvas ne reçoit pas ce clic : pas de trait parasite
    }

    private void CanvasContainer_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;

        Point currentPos = e.GetPosition(CanvasContainer);
        Vector delta = currentPos - _panStart;
        _panStart = currentPos;

        // Translation exprimée en pixels écran : le déplacement suit exactement la souris, dans toutes les directions
        ControlTranslate.X += delta.X;
        ControlTranslate.Y += delta.Y;
        OnViewChanged();
        e.Handled = true;
    }

    private void CanvasContainer_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning) return;
        CanvasContainer.ReleaseMouseCapture(); // déclenche LostMouseCapture → EndPan
        e.Handled = true;
    }

    private void CanvasContainer_LostMouseCapture(object sender, MouseEventArgs e) => EndPan();

    private void EndPan()
    {
        if (!_isPanning) return;
        _isPanning = false;
        ApplyTool();
    }

    // =====================================================================
    // TEXTE : saisi dans une zone de texte posée sur la feuille, puis converti en contours vectoriels
    // (Border transparent + Path étiré : redimensionnable comme une image, rendu identique à l'export)
    // =====================================================================
    private static IEnumerable<Border> TextsOf(IEnumerable<UIElement> elements) =>
        elements.OfType<Border>().Where(b => b.Tag is BoardTextTag);

    private static BoardTextData TextDataOf(Border element)
    {
        var tag = (BoardTextTag)element.Tag;
        return new BoardTextData(tag.Id, tag.Text, BoardHistory.BoundsOf(element), tag.FontSize, tag.Color);
    }

    private static Border CreateTextElement(BoardTextData text)
    {
        var element = new Border
        {
            Background = Brushes.Transparent, // toute la zone est cliquable, pas seulement les glyphes
            Tag = new BoardTextTag(text.Id, text.Text, text.FontSize, text.Color),
            Child = new System.Windows.Shapes.Path
            {
                Data = BoardText.BuildGeometry(text.Text, text.FontSize),
                Fill = new SolidColorBrush(text.Color),
                Stretch = Stretch.Fill
            }
        };
        BoardHistory.Apply(element, text.Bounds);
        return element;
    }

    /// <summary>Décalage entre le coin d'une TextBox (bordure 1, sans marge intérieure) et l'origine du texte.</summary>
    private static readonly Vector TextBoxInset = new(3, 1);

    /// <summary>
    /// Outil Texte : clic sur un texte → le modifier ; clic ailleurs → terminer la saisie en cours, sinon en commencer une.
    /// Outil Sélection : double-clic sur un texte → le modifier. Renvoie vrai si le clic est consommé.
    /// </summary>
    private bool HandleTextClick(MouseButtonEventArgs e)
    {
        // Clic dans la zone en cours de saisie : la TextBox le gère
        if (_textEditor != null && e.OriginalSource is Visual v && _textEditor.IsAncestorOf(v)) return true;

        bool textTool = _tool == Tool.Text && !_spaceHeld;
        bool editFromSelect = _tool == Tool.Select && e.ClickCount == 2;
        if (!textTool && !editFromSelect) return false;

        Point p = e.GetPosition(MyInkCanvas);
        var hit = TextsOf(MyInkCanvas.Children.Cast<UIElement>())
            .LastOrDefault(t => t.Visibility == Visibility.Visible && BoardHistory.BoundsOf(t).Contains(p));

        if (editFromSelect)
        {
            if (hit == null) return false;
            SelectTool(Tool.Text);
        }

        bool wasEditing = _textEditor != null;
        CommitTextEdit();
        if (hit != null) BeginTextEdit(hit);
        else if (!wasEditing) BeginTextEdit(p - TextBoxInset, "", _settings.TextSize, CurrentColor, null);
        e.Handled = true;
        return true;
    }

    /// <summary>Modifie un texte existant : la zone de saisie reprend sa taille apparente et sa couleur.</summary>
    private void BeginTextEdit(Border element)
    {
        var tag = (BoardTextTag)element.Tag;
        var bounds = BoardHistory.BoundsOf(element);
        var gb = BoardText.BuildGeometry(tag.Text, tag.FontSize).Bounds;
        double scale = gb.Height > 0 ? bounds.Height / gb.Height : 1;
        double fontSize = Math.Clamp(tag.FontSize * scale, BoardText.MinFontSize, BoardText.MaxFontSize);
        // Origine du texte telle qu'affiché (les contours commencent à gb.X, gb.Y de l'origine)
        var origin = new Point(bounds.X - gb.X * scale, bounds.Y - gb.Y * scale);
        BeginTextEdit(origin - TextBoxInset, tag.Text, fontSize, tag.Color, element);
    }

    private void BeginTextEdit(Point topLeft, string text, double fontSize, Color color, Border? editing)
    {
        var brush = new SolidColorBrush(color);
        var box = new TextBox
        {
            Text = text,
            FontFamily = new FontFamily(BoardText.FontFamilyName),
            FontSize = fontSize,
            Foreground = brush,
            CaretBrush = brush,
            Background = Brushes.Transparent,
            BorderBrush = (Brush)FindResource("AccentBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            AcceptsReturn = true,
            AcceptsTab = false,
            MinWidth = fontSize * 1.5,
            ToolTip = "Entrée : nouvelle ligne — Échap ou clic ailleurs : terminer"
        };
        InkCanvas.SetLeft(box, topLeft.X);
        InkCanvas.SetTop(box, topLeft.Y);
        box.PreviewKeyDown += (_, k) =>
        {
            if (k.Key == Key.Escape || (k.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control))
            {
                k.Handled = true;
                CommitTextEdit();
            }
        };
        box.LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(() => { if (_textEditor == box) CommitTextEdit(); });

        if (editing != null) editing.Visibility = Visibility.Collapsed;
        MyInkCanvas.Children.Add(box);
        (_textEditor, _editingText, _textEditorColor) = (box, editing, color);

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            box.Focus();
            Keyboard.Focus(box);
            box.CaretIndex = box.Text.Length;
        });
    }

    /// <summary>Termine la saisie : crée, remplace ou supprime le texte (une seule étape d'annulation).</summary>
    private void CommitTextEdit()
    {
        if (_textEditor is not { } box) return;
        var editing = _editingText;
        (_textEditor, _editingText) = (null, null);

        bool hadFocus = box.IsKeyboardFocusWithin;
        MyInkCanvas.Children.Remove(box);
        string text = box.Text.TrimEnd();
        if (editing != null) editing.Visibility = Visibility.Visible;

        var oldTag = editing?.Tag as BoardTextTag;
        bool unchanged = oldTag != null && oldTag.Text == text && oldTag.Color == _textEditorColor;
        if (!unchanged && (editing != null || !string.IsNullOrWhiteSpace(text)))
        {
            bool ownGroup = !_history.IsGrouping;
            if (ownGroup) _history.BeginGroup();

            if (editing != null)
            {
                MyInkCanvas.Children.Remove(editing);
                _history.RecordElementRemoved(editing);
            }
            if (!string.IsNullOrWhiteSpace(text))
            {
                var gb = BoardText.BuildGeometry(text, box.FontSize).Bounds;
                var origin = new Point(InkCanvas.GetLeft(box), InkCanvas.GetTop(box)) + TextBoxInset;
                var element = CreateTextElement(new BoardTextData(
                    oldTag?.Id ?? BoardFile.NewId(), text,
                    new Rect(origin.X + gb.X, origin.Y + gb.Y, gb.Width, gb.Height),
                    box.FontSize, _textEditorColor));
                MyInkCanvas.Children.Add(element);
                _history.RecordElementAdded(element);
            }

            if (ownGroup) _history.EndGroup();
        }

        if (hadFocus) CanvasContainer.Focus();
    }

    // =====================================================================
    // FORMES : tracées par glisser, ce sont de vrais traits d'encre
    // (donc gomme, sélection, déplacement, redimensionnement, annuler, enregistrement et export fonctionnent)
    // =====================================================================
    private void InkCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (HandleTextClick(e)) return;
        if (!IsShapeTool(_tool)) return;

        _shapeTool = _tool;
        _shapeStart = e.GetPosition(MyInkCanvas);
        _shapeStroke = new Stroke(BuildShapePoints(_shapeTool, _shapeStart, _shapeStart, false, _settings.ShapeThickness), ShapeAttributes());
        _shapeStroke.AddPropertyData(ShapeKindProperty, _shapeTool.ToString());
        MyInkCanvas.Strokes.Add(_shapeStroke); // enregistré dans l'historique ; les points suivent ensuite le glisser
        MyInkCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void InkCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_shapeStroke == null) return;
        bool constrain = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        _shapeStroke.StylusPoints = BuildShapePoints(_shapeTool, _shapeStart, e.GetPosition(MyInkCanvas), constrain, _settings.ShapeThickness);
        e.Handled = true;
    }

    private void InkCanvas_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_shapeStroke == null) return;

        Point end = e.GetPosition(MyInkCanvas);
        Vector drag = end - _shapeStart;

        // Simple clic (sans glisser) : forme de taille par défaut centrée sur le clic
        if (Math.Abs(drag.X) < 4 && Math.Abs(drag.Y) < 4)
        {
            var size = _shapeTool is Tool.Line or Tool.Arrow ? new Vector(140, 0) : new Vector(140, 100);
            var start = _shapeStart - size / 2;
            _shapeStroke.StylusPoints = BuildShapePoints(_shapeTool, start, start + size, false, _settings.ShapeThickness);
        }

        _shapeStroke = null;
        MyInkCanvas.ReleaseMouseCapture();
        e.Handled = true;
    }

    private DrawingAttributes ShapeAttributes() => new()
    {
        Color = CurrentColor,
        Width = _settings.ShapeThickness,
        Height = _settings.ShapeThickness,
        StylusTip = StylusTip.Ellipse,
        FitToCurve = false,     // angles nets
        IgnorePressure = true   // épaisseur régulière
    };

    /// <summary>Points du contour de la forme entre deux coins (Maj : carré/cercle, ou angle multiple de 45°).</summary>
    private static StylusPointCollection BuildShapePoints(Tool tool, Point a, Point b, bool constrain, double thickness)
    {
        var pts = new StylusPointCollection();
        void Add(double x, double y) => pts.Add(new StylusPoint(x, y));

        if (tool is Tool.Line or Tool.Arrow)
        {
            if (constrain) b = SnapTo45(a, b);
            Add(a.X, a.Y);
            Add(b.X, b.Y);

            Vector dir = b - a;
            if (tool == Tool.Arrow && dir.Length > 1)
            {
                double head = Math.Min(Math.Max(16, thickness * 5), dir.Length * 0.45);
                dir.Normalize();
                Point h1 = b + Rotate(-dir * head, 28);
                Point h2 = b + Rotate(-dir * head, -28);
                Add(h1.X, h1.Y);
                Add(b.X, b.Y);
                Add(h2.X, h2.Y);
            }
            return pts;
        }

        double dx = b.X - a.X, dy = b.Y - a.Y;
        if (constrain)
        {
            double side = Math.Max(Math.Abs(dx), Math.Abs(dy));
            dx = (dx < 0 ? -1 : 1) * side;
            dy = (dy < 0 ? -1 : 1) * side;
        }
        double left = Math.Min(a.X, a.X + dx), top = Math.Min(a.Y, a.Y + dy);
        double w = Math.Abs(dx), h = Math.Abs(dy), right = left + w, bottom = top + h;

        switch (tool)
        {
            case Tool.Rectangle:
                Add(left, top); Add(right, top); Add(right, bottom); Add(left, bottom); Add(left, top);
                break;
            case Tool.Ellipse:
                const int segments = 96;
                for (int i = 0; i <= segments; i++)
                {
                    double t = 2 * Math.PI * i / segments;
                    Add(left + w / 2 + w / 2 * Math.Cos(t), top + h / 2 + h / 2 * Math.Sin(t));
                }
                break;
            case Tool.Triangle:
                Add(left + w / 2, top); Add(right, bottom); Add(left, bottom); Add(left + w / 2, top);
                break;
        }
        return pts;
    }

    private static Point SnapTo45(Point a, Point b)
    {
        Vector v = b - a;
        double angle = Math.Round(Math.Atan2(v.Y, v.X) / (Math.PI / 4)) * (Math.PI / 4);
        return new Point(a.X + Math.Cos(angle) * v.Length, a.Y + Math.Sin(angle) * v.Length);
    }

    private static Vector Rotate(Vector v, double degrees)
    {
        double r = degrees * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
        return new Vector(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    private void ClearBoard(object sender, RoutedEventArgs e)
    {
        if (MyInkCanvas.Strokes.Count == 0 && MyInkCanvas.Children.Count == 0) return;

        if (_settings.ConfirmClear && MessageBox.Show(this, "Effacer tout le contenu du tableau ?\n(Ctrl+Z permet d'annuler.)", AppName,
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _history.BeginGroup();
        MyInkCanvas.Select(new StrokeCollection());
        MyInkCanvas.Strokes.Clear();
        foreach (var element in MyInkCanvas.Children.Cast<UIElement>().ToList())
        {
            MyInkCanvas.Children.Remove(element);
            _history.RecordElementRemoved(element);
        }
        _history.EndGroup();
    }

    // =====================================================================
    // IMAGES (PNG / JPEG importés, illustrations générées via MCP)
    // =====================================================================
    private IEnumerable<Image> ItemElements =>
        MyInkCanvas.Children.OfType<Image>().Where(i => i.Tag is BoardItemTag);

    private static BitmapSource DecodeImage(byte[] data)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = new MemoryStream(data);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static Image CreateItemElement(string id, byte[] data, string? label, BitmapSource source, Rect bounds)
    {
        var image = new Image
        {
            Source = source,
            Stretch = Stretch.Fill,
            Tag = new BoardItemTag(id, data, label),
            ToolTip = string.IsNullOrWhiteSpace(label) ? null : label
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        BoardHistory.Apply(image, bounds);
        return image;
    }

    /// <summary>
    /// Pose une image sur le tableau (proportions conservées).
    /// <paramref name="x"/>/<paramref name="y"/> = coin haut-gauche en coordonnées monde ; <paramref name="width"/> en unités monde
    /// (défaut : taille naturelle). Sans position, l'image est centrée dans la vue et réduite pour y tenir (60 % × 80 %).
    /// </summary>
    private string AddImageItem(byte[] data, string? label, double? x = null, double? y = null, double? width = null)
    {
        var source = DecodeImage(data); // lève une exception si le fichier n'est pas une image valide
        var view = VisibleWorldRect();

        double natW = Math.Max(1, source.Width), natH = Math.Max(1, source.Height);
        double w = width is > 0 ? width.Value : natW;
        double h = w * natH / natW;
        if (x == null && y == null)
        {
            double fit = Math.Min(1, Math.Min(view.Width * 0.6 / w, view.Height * 0.8 / h));
            w *= fit;
            h *= fit;
        }
        Point spot = x == null && y == null
            ? FindFreeSpot(new Size(w, h), view)
            : new Point(x ?? view.X + (view.Width - w) / 2, y ?? view.Y + (view.Height - h) / 2);
        var bounds = new Rect(spot, new Size(w, h));

        string id = BoardFile.NewId();
        var element = CreateItemElement(id, data, label, source, bounds);
        MyInkCanvas.Children.Add(element);
        _history.RecordElementAdded(element);

        if (!view.Contains(bounds)) CenterViewOn(bounds); // posée hors de la vue : on l'amène à l'écran
        return id;
    }

    /// <summary>
    /// Cherche un emplacement libre (sans encre ni image, avec une marge) pour un élément de taille donnée :
    /// d'abord dans la vue, au plus près du centre ; sinon à droite de tout le contenu de la page.
    /// </summary>
    private Point FindFreeSpot(Size size, Rect view)
    {
        const double gap = 24, step = 40;
        var occupied = MyInkCanvas.Strokes.Select(s => s.GetBounds())
            .Concat(ItemElements.Select(BoardHistory.BoundsOf))
            .Concat(TextsOf(MyInkCanvas.Children.Cast<UIElement>()).Select(BoardHistory.BoundsOf))
            .Select(r => { r.Inflate(gap, gap); return r; })
            .ToList();
        bool IsFree(Rect r) => !occupied.Any(o => o.IntersectsWith(r));

        var center = new Point(view.X + view.Width / 2, view.Y + view.Height / 2);
        var centered = new Rect(center.X - size.Width / 2, center.Y - size.Height / 2, size.Width, size.Height);
        if (IsFree(centered)) return centered.Location;

        var candidates = new List<Rect>();
        for (double top = view.Top + gap; top + size.Height <= view.Bottom - gap; top += step)
            for (double left = view.Left + gap; left + size.Width <= view.Right - gap; left += step)
                candidates.Add(new Rect(left, top, size.Width, size.Height));

        foreach (var r in candidates.OrderBy(r => (r.TopLeft + (Vector)r.Size / 2 - center).LengthSquared))
            if (IsFree(r)) return r.Location;

        // La vue est pleine : à droite de tout le contenu, à hauteur du centre de la vue
        var all = Rect.Empty;
        foreach (var o in occupied) all.Union(o);
        return new Point(all.Right, center.Y - size.Height / 2);
    }

    /// <summary>Centre la vue sur une zone du monde (échelle inchangée).</summary>
    private void CenterViewOn(Rect area)
    {
        ControlTranslate.X = CanvasContainer.ActualWidth / 2 - (area.X + area.Width / 2) * Scale;
        ControlTranslate.Y = CanvasContainer.ActualHeight / 2 - (area.Y + area.Height / 2) * Scale;
        OnViewChanged();
    }

    private void ImportImage_Click(object sender, RoutedEventArgs e) => ImportImages();

    private void ImportImages()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Importer des images",
            Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;
        ImportFiles(dialog.FileNames, null);
    }

    private void ImportFiles(IEnumerable<string> files, Point? at)
    {
        _history.BeginGroup();
        int index = 0;
        foreach (var file in files)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is not (".png" or ".jpg" or ".jpeg")) continue;
            try
            {
                Point? pos = at is { } p ? p + new Vector(index * 30, index * 30) : null;
                AddImageItem(File.ReadAllBytes(file), Path.GetFileNameWithoutExtension(file), pos?.X, pos?.Y);
                index++;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Impossible d'importer « {Path.GetFileName(file)} » :\n{ex.Message}", AppName,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        _history.EndGroup();
    }

    private void CanvasContainer_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            ImportFiles(files, e.GetPosition(MyInkCanvas));
    }

    private void PasteFromClipboard()
    {
        if (Clipboard.ContainsFileDropList())
        {
            ImportFiles(Clipboard.GetFileDropList().Cast<string>(), null);
        }
        else if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            AddImageItem(ms.ToArray(), "Image collée");
        }
    }

    // =====================================================================
    // FICHIERS : Nouveau / Ouvrir / Enregistrer (.gboard) et Export
    // =====================================================================
    private void New_Click(object sender, RoutedEventArgs e) => NewBoard();
    private void Open_Click(object sender, RoutedEventArgs e) => OpenBoard();
    private void Export_Click(object sender, RoutedEventArgs e) => ExportBoard();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) SaveAs();
        else Save();
    }

    private void MarkDirty()
    {
        if (_dirty) return;
        _dirty = true;
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        string name = _currentFile == null ? "Sans titre" : Path.GetFileNameWithoutExtension(_currentFile);
        string cloudSuffix = _cloudAccount.IsConnected ? " ☁" : "";
        Title = $"{name}{(_dirty ? " •" : "")}{cloudSuffix} — {AppName}";
    }

    private static IEnumerable<Image> ItemsOf(IEnumerable<UIElement> elements) =>
        elements.OfType<Image>().Where(i => i.Tag is BoardItemTag);

    private BoardDocument Snapshot()
    {
        StoreActivePage();
        var pages = _pages.Select(p => new BoardPageData(
            p.Id, p.Title, p.Strokes,
            ItemsOf(p.Elements).Select(i =>
            {
                var tag = (BoardItemTag)i.Tag;
                return new BoardItemData(tag.Id, tag.Data, BoardHistory.BoundsOf(i), tag.Label);
            }).ToList(),
            p.Background, p.Scale, p.TranslateX, p.TranslateY,
            TextsOf(p.Elements).Select(TextDataOf).ToList())).ToList();
        return new BoardDocument(pages, _pages.IndexOf(Page));
    }

    private void LoadDocument(BoardDocument doc, string? path)
    {
        _page?.History.Detach();
        MyInkCanvas.Select(new StrokeCollection());
        _pages.Clear();

        foreach (var data in doc.Pages)
        {
            var page = CreatePage(data.Id, data.Title, data.Strokes, data.Background);
            foreach (var item in data.Items)
            {
                try { page.Elements.Add(CreateItemElement(item.Id, item.Data, item.Label, DecodeImage(item.Data), item.Bounds)); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Image ignorée : {ex.Message}"); }
            }
            foreach (var text in data.Texts ?? [])
                page.Elements.Add(CreateTextElement(text));
            page.Scale = Math.Clamp(data.Scale, MinScale, MaxScale);
            page.TranslateX = data.TranslateX;
            page.TranslateY = data.TranslateY;
            // Vue (0, 0) = jamais enregistrée (nouveau tableau) : on centrera la feuille à l'affichage
            page.HasView = data.TranslateX != 0 || data.TranslateY != 0;
            _pages.Add(page);
        }

        _page = null;
        ActivatePage(_pages[Math.Clamp(doc.ActivePage, 0, _pages.Count - 1)], storeCurrent: false);

        _currentFile = path;
        _dirty = false;
        UpdateTitle();
    }

    /// <summary>Demande d'enregistrer les modifications. Renvoie false si l'utilisateur annule.</summary>
    private bool ConfirmSaveChanges()
    {
        if (!_dirty) return true;
        var answer = MessageBox.Show(this, "Enregistrer les modifications du tableau ?", AppName,
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer switch
        {
            MessageBoxResult.Yes => Save(),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    private void NewBoard()
    {
        if (!ConfirmSaveChanges()) return;
        LoadDocument(new BoardDocument(
            [new BoardPageData(BoardFile.NewId(), "Page 1", new StrokeCollection(), [], _settings.DefaultBackground, 1, 0, 0)], 0), null);
    }

    private void OpenFromCommandLine(IEnumerable<string> args)
    {
        string? path = args.FirstOrDefault(a =>
            a.EndsWith(BoardFile.Extension, StringComparison.OrdinalIgnoreCase) && File.Exists(a));
        if (path == null) return;

        try
        {
            LoadDocument(BoardFile.Load(path), Path.GetFullPath(path));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Impossible d'ouvrir « {Path.GetFileName(path)} » :\n{ex.Message}", AppName,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenBoard()
    {
        if (!ConfirmSaveChanges()) return;
        var dialog = new OpenFileDialog { Title = "Ouvrir un tableau", Filter = BoardFile.DialogFilter };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            LoadDocument(BoardFile.Load(dialog.FileName), dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Impossible d'ouvrir ce tableau :\n{ex.Message}", AppName,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool Save() => _currentFile == null ? SaveAs() : SaveTo(_currentFile);

    private bool SaveAs()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Enregistrer le tableau",
            Filter = BoardFile.DialogFilter,
            DefaultExt = BoardFile.Extension,
            FileName = _currentFile == null ? "Tableau" : Path.GetFileName(_currentFile)
        };
        return dialog.ShowDialog(this) == true && SaveTo(dialog.FileName);
    }

    private bool SaveTo(string path)
    {
        try
        {
            BoardFile.Save(path, Snapshot());
            _currentFile = path;
            _dirty = false;
            UpdateTitle();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Échec de l'enregistrement :\n{ex.Message}", AppName,
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>Toutes les pages, prêtes pour l'export (la page affichée est d'abord mémorisée).</summary>
    private List<BoardExporter.Page> BuildExportPages()
    {
        StoreActivePage();
        return _pages.Select(p => new BoardExporter.Page(
            p.Strokes,
            ItemsOf(p.Elements)
                .Select(i => new BoardExporter.Item((BitmapSource)i.Source, BoardHistory.BoundsOf(i), ((BoardItemTag)i.Tag).Data))
                .ToList(),
            p.Background,
            TextsOf(p.Elements).Select(TextDataOf).ToList())).ToList();
    }

    // =====================================================================
    // PARTAGE PAR E-MAIL (PDF joint à un nouveau message de la messagerie de l'utilisateur)
    // =====================================================================
    private void Share_Click(object sender, RoutedEventArgs e) => _ = ShareBoardAsync();

    private async Task ShareBoardAsync()
    {
        var pages = BuildExportPages();
        int active = _pages.IndexOf(Page);
        if (pages.All(p => p.IsEmpty))
        {
            MessageBox.Show(this, "Le tableau est vide : rien à partager.", AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string boardName = _currentFile == null ? Page.Title : Path.GetFileNameWithoutExtension(_currentFile);
        var dialog = new Views.ShareWindow(boardName, Page.Title, _pages.Count, MailShare.LoadRecentRecipients()) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } request) return;

        if (!request.AllPages && pages[active].IsEmpty)
        {
            MessageBox.Show(this, $"La page « {Page.Title} » est vide : rien à partager.", AppName,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 1. Export du PDF dans un dossier temporaire dédié
        string pdfPath;
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            string folder = Path.Combine(Path.GetTempPath(), "GreenBoard", "Partage");
            Directory.CreateDirectory(folder);
            pdfPath = Path.Combine(folder, request.FileName + ".pdf");
            if (request.AllPages) BoardExporter.Export(pdfPath, pages, active);
            else BoardExporter.Export(pdfPath, [pages[active]], 0);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Échec de l'export PDF :\n{ex.Message}", AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        MailShare.SaveRecentRecipients(request.Recipients);

        // 2. Nouveau message dans la messagerie (Simple MAPI). Appel bloquant chez certains clients
        //    tant que la fenêtre de rédaction est ouverte : thread STA dédié pour garder GreenBoard réactif.
        var outcome = await RunOnStaThread(() => MailShare.ComposeWithMailClient(request, pdfPath, out _));

        // 3. Repli : mailto (destinataires, objet, texte) + dossier du PDF pour le glisser dans le message
        if (outcome == MailShare.MapiOutcome.Unavailable)
        {
            try
            {
                MailShare.OpenMailtoAndRevealFile(request, pdfPath);
                MessageBox.Show(this,
                    "Votre messagerie ne permet pas de joindre le fichier automatiquement.\n\n" +
                    "Un nouveau message a été ouvert avec les destinataires et l'objet, et le dossier contenant " +
                    $"« {Path.GetFileName(pdfPath)} » est affiché : glissez simplement le PDF dans le message.",
                    AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Aucune messagerie n'a pu être ouverte ({ex.Message}).\nLe PDF est prêt ici :\n{pdfPath}",
                    AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private static Task<T> RunOnStaThread<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>();
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    private void ExportBoard()
    {
        var pages = BuildExportPages();
        int active = _pages.IndexOf(Page);
        if (pages.All(p => p.IsEmpty))
        {
            MessageBox.Show(this, "Le tableau est vide : rien à exporter.", AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = _pages.Count > 1
                ? $"Exporter — PDF : les {_pages.Count} pages ; PNG, JPEG, SVG : la page « {Page.Title} »"
                : "Exporter le tableau",
            Filter = BoardExporter.DialogFilter,
            FileName = _currentFile == null ? "Tableau" : Path.GetFileNameWithoutExtension(_currentFile)
        };
        if (dialog.ShowDialog(this) != true) return;

        bool isPdf = Path.GetExtension(dialog.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
        if (!isPdf && pages[active].IsEmpty)
        {
            MessageBox.Show(this, $"La page « {Page.Title} » est vide : rien à exporter.", AppName,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            BoardExporter.Export(dialog.FileName, pages, active);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Échec de l'export :\n{ex.Message}", AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!ConfirmSaveChanges()) e.Cancel = true;
    }

    // =====================================================================
    // COMMANDES DU SERVEUR MCP (via le canal nommé local)
    // =====================================================================
    private BoardResponse HandleBoardRequest(BoardRequest request)
    {
        try
        {
            switch (request.Command)
            {
                case BoardProtocol.Commands.GetBoard:
                    return new BoardResponse(true, Board: DescribeBoard());

                case BoardProtocol.Commands.AddImage:
                {
                    if (string.IsNullOrEmpty(request.ImageBase64)) return BoardResponse.Fail("imageBase64 manquant.");
                    var data = Convert.FromBase64String(request.ImageBase64);
                    _history.EndGroup();
                    string id = AddImageItem(data, request.Label, request.X, request.Y, request.Width);
                    return new BoardResponse(true, ItemId: id, Board: DescribeBoard());
                }

                case BoardProtocol.Commands.RemoveItem:
                {
                    var element = ItemElements.FirstOrDefault(i => ((BoardItemTag)i.Tag).Id == request.ItemId);
                    if (element == null) return BoardResponse.Fail($"Élément introuvable : {request.ItemId}");
                    _history.EndGroup();
                    MyInkCanvas.Select(new StrokeCollection());
                    MyInkCanvas.Children.Remove(element);
                    _history.RecordElementRemoved(element);
                    return new BoardResponse(true, ItemId: request.ItemId, Board: DescribeBoard());
                }

                case BoardProtocol.Commands.SetBackground:
                    if (!Enum.TryParse(request.Background, ignoreCase: true, out BoardBackground bg))
                        return BoardResponse.Fail($"Arrière-plan inconnu. Valeurs : {string.Join(", ", BoardProtocol.Backgrounds)}");
                    SetBackground(bg, markDirty: true);
                    return new BoardResponse(true, Board: DescribeBoard());

                case BoardProtocol.Commands.AddPage:
                    AddPage(request.Label);
                    return new BoardResponse(true, Board: DescribeBoard());

                case BoardProtocol.Commands.SelectPage:
                    if (request.PageIndex is not { } index || index < 0 || index >= _pages.Count)
                        return BoardResponse.Fail($"Page inexistante. Pages : 0 à {_pages.Count - 1}.");
                    ActivatePage(_pages[index]);
                    return new BoardResponse(true, Board: DescribeBoard());

                default:
                    return BoardResponse.Fail($"Commande inconnue : {request.Command}");
            }
        }
        catch (Exception ex)
        {
            return BoardResponse.Fail(ex.Message);
        }
    }

    private BoardInfo DescribeBoard()
    {
        var view = VisibleWorldRect();
        var items = ItemElements.Select(i =>
        {
            var tag = (BoardItemTag)i.Tag;
            var b = BoardHistory.BoundsOf(i);
            return new BoardItemInfo(tag.Id, tag.Label, Math.Round(b.X), Math.Round(b.Y), Math.Round(b.Width), Math.Round(b.Height));
        }).ToList();
        var pages = _pages.Select((p, i) => new BoardPageInfo(i, p.Title, p.Strokes.Count,
            p == _page ? items.Count : ItemsOf(p.Elements).Count())).ToList();
        return new BoardInfo(_background.ToString(), Math.Round(Scale, 3),
            Math.Round(view.X), Math.Round(view.Y), Math.Round(view.Width), Math.Round(view.Height),
            MyInkCanvas.Strokes.Count, items, _pages.IndexOf(Page), pages);
    }

    // =====================================================================
    // PAGES (onglets au titre modifiable)
    // =====================================================================
    private BoardPage CreatePage(string id, string title, StrokeCollection strokes, BoardBackground background)
    {
        var page = new BoardPage(id, title, strokes, new BoardHistory(MyInkCanvas)) { Background = background };
        page.History.Changed += (_, _) =>
        {
            if (page != _page) return;
            MarkDirty();
            UpdateUndoButtons();
            UpdateRotateHandle(); // annuler/rétablir peut déplacer ou retirer la sélection
        };
        return page;
    }

    /// <summary>Mémorise l'état de la page affichée (éléments, arrière-plan, vue).</summary>
    private void StoreActivePage()
    {
        if (_page == null) return;
        CommitTextEdit(); // un texte en cours de saisie fait partie de la page
        _page.Elements = MyInkCanvas.Children.Cast<UIElement>().ToList();
        _page.Background = _background;
        _page.Scale = Scale;
        _page.TranslateX = ControlTranslate.X;
        _page.TranslateY = ControlTranslate.Y;
        _page.HasView = true;
    }

    /// <summary>Affiche une page : ses traits, ses images, son arrière-plan, sa vue et son historique.</summary>
    private void ActivatePage(BoardPage page, bool storeCurrent = true)
    {
        if (page == _page) return;
        if (storeCurrent && _page != null)
        {
            StoreActivePage();
            _page.History.Detach();
        }

        // Interrompt un éventuel tracé de forme ou déplacement
        _shapeStroke = null;
        if (_isPanning) CanvasContainer.ReleaseMouseCapture();

        _page = page;
        MyInkCanvas.Select(new StrokeCollection());
        MyInkCanvas.Strokes = page.Strokes;
        MyInkCanvas.Children.Clear();
        foreach (var element in page.Elements) MyInkCanvas.Children.Add(element);
        page.History.Attach();

        SetBackground(page.Background, markDirty: false);
        if (page.HasView)
        {
            ControlScale.ScaleX = ControlScale.ScaleY = page.Scale;
            ControlTranslate.X = page.TranslateX;
            ControlTranslate.Y = page.TranslateY;
            OnViewChanged();
        }
        else
        {
            ResetView();
        }

        UpdateUndoButtons();
        RebuildTabs();
    }

    private void AddPage_Click(object sender, RoutedEventArgs e) => AddPage();

    /// <summary>Insère une page après la page active (même arrière-plan) et l'affiche.</summary>
    private BoardPage AddPage(string? title = null)
    {
        int n = _pages.Count + 1;
        while (_pages.Any(p => p.Title == $"Page {n}")) n++;
        string name = string.IsNullOrWhiteSpace(title) ? $"Page {n}" : CleanTitle(title);

        var page = CreatePage(BoardFile.NewId(), name, new StrokeCollection(), _background);
        _pages.Insert(_pages.IndexOf(Page) + 1, page);
        ActivatePage(page);
        MarkDirty();
        return page;
    }

    private void DuplicatePage(BoardPage source)
    {
        if (source == _page) StoreActivePage();
        var copy = CreatePage(BoardFile.NewId(), CleanTitle($"{source.Title} (copie)"), source.Strokes.Clone(), source.Background);
        foreach (var image in ItemsOf(source.Elements))
        {
            var tag = (BoardItemTag)image.Tag;
            copy.Elements.Add(CreateItemElement(BoardFile.NewId(), tag.Data, tag.Label, (BitmapSource)image.Source,
                BoardHistory.BoundsOf(image)));
        }
        foreach (var text in TextsOf(source.Elements))
            copy.Elements.Add(CreateTextElement(TextDataOf(text) with { Id = BoardFile.NewId() }));
        (copy.Scale, copy.TranslateX, copy.TranslateY, copy.HasView) =
            (source.Scale, source.TranslateX, source.TranslateY, source.HasView);

        _pages.Insert(_pages.IndexOf(source) + 1, copy);
        ActivatePage(copy);
        MarkDirty();
    }

    private void DeletePage(BoardPage page)
    {
        if (_pages.Count == 1)
        {
            MessageBox.Show(this, "C'est la seule page du tableau : elle ne peut pas être supprimée.\n" +
                                  "Utilisez « Tout effacer » pour la vider.", AppName,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (page == _page) StoreActivePage();
        bool hasContent = page.Strokes.Count > 0 || page.Elements.Count > 0;
        if (hasContent && MessageBox.Show(this, $"Supprimer la page « {page.Title} » et tout son contenu ?\n" +
                                               "Cette action ne peut pas être annulée.", AppName,
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        int index = _pages.IndexOf(page);
        _pages.Remove(page);
        if (page == _page)
        {
            page.History.Detach();
            _page = null;
            ActivatePage(_pages[Math.Min(index, _pages.Count - 1)], storeCurrent: false);
        }
        else
        {
            RebuildTabs();
        }
        MarkDirty();
    }

    private void CyclePage(int delta)
    {
        if (_pages.Count < 2) return;
        int index = (_pages.IndexOf(Page) + delta + _pages.Count) % _pages.Count;
        ActivatePage(_pages[index]);
    }

    private static string CleanTitle(string title)
    {
        string t = string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return t.Length <= 60 ? t : t[..60];
    }

    private void RenamePage(BoardPage page, string title)
    {
        string t = CleanTitle(title);
        if (t.Length > 0 && t != page.Title)
        {
            page.Title = t;
            MarkDirty();
        }
        RebuildTabs();
    }

    /// <summary>Passe l'onglet en mode édition (double-clic, F2 ou menu « Renommer »).</summary>
    private void BeginRename(BoardPage page)
    {
        if (!_tabParts.TryGetValue(page, out var parts)) return;
        parts.Title.Visibility = Visibility.Collapsed;
        parts.Editor.Visibility = Visibility.Visible;
        parts.Editor.Text = page.Title;
        parts.Editor.Focus();
        parts.Editor.SelectAll();
    }

    private void TabsScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        TabsScroller.ScrollToHorizontalOffset(TabsScroller.HorizontalOffset - e.Delta / 2.0);
        e.Handled = true;
    }

    /// <summary>Reconstruit la barre d'onglets à partir de la liste des pages.</summary>
    private void RebuildTabs()
    {
        if (TabsPanel == null) return;
        TabsPanel.Children.Clear();
        _tabParts.Clear();

        var accent = (Brush)FindResource("AccentBrush");
        var hover = new SolidColorBrush(Color.FromRgb(0xE2, 0xE5, 0xEA));
        Border? activeTab = null;

        foreach (var page in _pages)
        {
            bool active = page == _page;

            var title = new TextBlock
            {
                Text = page.Title,
                FontSize = 13,
                MaxWidth = 200,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = new SolidColorBrush(active ? Color.FromRgb(0x1F, 0x29, 0x37) : Color.FromRgb(0x4B, 0x55, 0x63))
            };
            var editor = new TextBox
            {
                FontSize = 13,
                MinWidth = 90,
                MaxWidth = 220,
                MaxLength = 60,
                Padding = new Thickness(2, 1, 2, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            bool committed = false;
            void Commit(bool accept)
            {
                if (committed) return;
                committed = true;
                if (accept) RenamePage(page, editor.Text);
                else RebuildTabs();
                CanvasContainer.Focus();
            }
            editor.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { Commit(true); e.Handled = true; }
                else if (e.Key == Key.Escape) { Commit(false); e.Handled = true; }
            };
            editor.LostKeyboardFocus += (_, _) => Commit(true);

            var close = new Button
            {
                Content = FindResource("IconClose"),
                Style = (Style)FindResource("TabCloseButtonStyle"),
                ToolTip = "Supprimer la page",
                Margin = new Thickness(6, 0, 0, 0),
                Visibility = active ? Visibility.Visible : Visibility.Hidden
            };
            close.Click += (_, _) => DeletePage(page);

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(title);
            row.Children.Add(editor);
            row.Children.Add(close);

            var tab = new Border
            {
                Child = row,
                Height = 32,
                Padding = new Thickness(12, 0, 6, 0),
                Margin = new Thickness(0, 0, 2, 0),
                CornerRadius = new CornerRadius(8, 8, 0, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = active ? Brushes.White : Brushes.Transparent,
                BorderBrush = active ? accent : null,
                BorderThickness = new Thickness(0, active ? 2 : 0, 0, 0),
                Cursor = Cursors.Hand,
                ToolTip = $"{page.Title}\nDouble-clic ou F2 : renommer · clic milieu : supprimer"
            };
            if (!active)
            {
                tab.MouseEnter += (_, _) => { tab.Background = hover; close.Visibility = Visibility.Visible; };
                tab.MouseLeave += (_, _) => { tab.Background = Brushes.Transparent; close.Visibility = Visibility.Hidden; };
            }
            tab.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2) BeginRename(page);
                else ActivatePage(page);
                e.Handled = true;
            };
            tab.MouseDown += (_, e) =>
            {
                if (e.ChangedButton == MouseButton.Middle) { DeletePage(page); e.Handled = true; }
            };

            var menu = new ContextMenu();
            void AddMenu(string header, Action action, string? gesture = null)
            {
                var item = new MenuItem { Header = header, InputGestureText = gesture ?? "" };
                item.Click += (_, _) => action();
                menu.Items.Add(item);
            }
            AddMenu("Renommer", () =>
            {
                ActivatePage(page);
                // Après la fermeture du menu (qui reprend le focus), sinon l'édition se validerait aussitôt
                Dispatcher.BeginInvoke(() => BeginRename(page), System.Windows.Threading.DispatcherPriority.Input);
            }, "F2");
            AddMenu("Dupliquer", () => DuplicatePage(page));
            AddMenu("Nouvelle page", () => { ActivatePage(page); AddPage(); }, "Ctrl+T");
            menu.Items.Add(new Separator());
            AddMenu("Supprimer", () => DeletePage(page));
            tab.ContextMenu = menu;

            _tabParts[page] = (title, editor);
            TabsPanel.Children.Add(tab);
            if (active) activeTab = tab;
        }

        // Garde l'onglet actif visible quand il y a beaucoup de pages
        if (activeTab != null)
            Dispatcher.BeginInvoke(() => activeTab.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // =====================================================================
    // ARRIÈRE-PLANS (motif infini qui suit le zoom et le déplacement)
    // =====================================================================
    private void BuildBackgroundChoices()
    {
        foreach (BoardBackground bg in Enum.GetValues<BoardBackground>())
        {
            // Aperçu à 60 % pour voir plusieurs carreaux dans la vignette
            Brush preview = BoardBackgrounds.CreateBrush(bg, 0.6);
            if (preview is TileBrush tile) tile.Transform = new ScaleTransform(0.6, 0.6);

            var swatch = new Border
            {
                Width = 96,
                Height = 62,
                CornerRadius = new CornerRadius(6),
                Background = preview,
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent
            };
            _backgroundPreviews[bg] = swatch;

            var panel = new StackPanel();
            panel.Children.Add(new Border
            {
                Child = swatch,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE5, 0xEA))
            });
            panel.Children.Add(new TextBlock
            {
                Text = BoardBackgrounds.Label(bg),
                Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x3C, 0x43, 0x50)),
                FontSize = 12
            });

            var button = new Button
            {
                Content = panel,
                Margin = new Thickness(4),
                Focusable = false,
                Cursor = Cursors.Hand,
                Template = new ControlTemplate(typeof(Button))
                {
                    VisualTree = new FrameworkElementFactory(typeof(ContentPresenter))
                }
            };
            button.Click += (_, _) =>
            {
                SetBackground(bg, markDirty: true);
                BtnBackground.IsChecked = false;
            };
            BackgroundChoices.Children.Add(button);
        }
        HighlightBackgroundChoice();
    }

    private void HighlightBackgroundChoice()
    {
        var accent = (Brush)FindResource("AccentBrush");
        foreach (var (bg, swatch) in _backgroundPreviews)
            swatch.BorderBrush = bg == _background ? accent : Brushes.Transparent;
    }

    private void SetBackground(BoardBackground bg, bool markDirty)
    {
        bool wasDark = BoardBackgrounds.IsDark(_background);
        bool changed = bg != _background;
        _background = bg;
        _backgroundBuiltScale = double.NaN; // force la reconstruction du motif
        UpdateBackgroundBrush();
        HighlightBackgroundChoice();
        if (markDirty && changed) MarkDirty();

        // Encre lisible automatiquement : craie blanche sur le tableau vert, noir sur fond clair
        if (BoardBackgrounds.IsDark(bg) && !wasDark && CurrentColor == (Color)ColorConverter.ConvertFromString("#1F2328"))
            ColorWhite.IsChecked = true;
        else if (!BoardBackgrounds.IsDark(bg) && wasDark && CurrentColor == Colors.White)
            ColorBlack.IsChecked = true;
    }

    // Popup : pendant qu'il est ouvert, le bouton ne doit pas capter le clic qui le referme (sinon il se rouvre)
    private void BackgroundPopup_Opened(object? sender, EventArgs e) => BtnBackground.IsHitTestVisible = false;
    private void BackgroundPopup_Closed(object? sender, EventArgs e) => BtnBackground.IsHitTestVisible = true;

    /// <summary>
    /// Le motif est peint sur le conteneur (donc infini) et suit la même transformation que la feuille.
    /// Il n'est reconstruit qu'au changement d'échelle, pour garder des traits fins à tout niveau de zoom.
    /// </summary>
    private void UpdateBackgroundBrush()
    {
        if (CanvasContainer == null) return;

        if (_backgroundBuiltScale != Scale)
        {
            _backgroundBrush = BoardBackgrounds.CreateBrush(_background, Scale);
            _backgroundBuiltScale = Scale;
            CanvasContainer.Background = _backgroundBrush;
        }

        if (_backgroundBrush is TileBrush tile)
            tile.Transform = new MatrixTransform(Scale, 0, 0, Scale, ControlTranslate.X, ControlTranslate.Y);
    }
}
