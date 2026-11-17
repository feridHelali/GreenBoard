using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace InfiniteCanvas.App.Views;

/// <summary>
/// Réglages de GreenBoard. Chaque modification est appliquée tout de suite au tableau (aperçu en direct) ;
/// « Annuler » ou la fermeture de la fenêtre rétablit les réglages d'origine.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _original;
    private readonly Action<AppSettings> _apply;
    private readonly CloudClient _cloudClient;
    private CloudAccount _cloudAccount;
    private bool _loading;
    private bool _saved;

    public SettingsWindow(AppSettings current, Action<AppSettings> apply, CloudClient cloudClient, CloudAccount cloudAccount)
    {
        InitializeComponent();
        _original = current;
        _apply = apply;
        _cloudClient = cloudClient;
        _cloudAccount = cloudAccount;

        foreach (var bg in Enum.GetValues<BoardBackground>())
            BackgroundCombo.Items.Add(new ComboBoxItem { Content = BoardBackgrounds.Label(bg), Tag = bg });
        foreach (int minutes in AppSettings.AutoSaveChoices)
            AutoSaveCombo.Items.Add(new ComboBoxItem
            {
                Content = minutes == 0 ? "Désactivée" : minutes == 1 ? "Toutes les minutes" : $"Toutes les {minutes} minutes",
                Tag = minutes
            });

        Show(current);
        RefreshAccountStatus();
        Closed += (_, _) => { if (!_saved) _apply(_original); };
    }

    /// <summary>Réglages choisis (valides après « Enregistrer »).</summary>
    public AppSettings Result { get; private set; } = new();

    private void Show(AppSettings s)
    {
        _loading = true;
        PenSlider.Value = s.PenThickness;
        ShapeSlider.Value = s.ShapeThickness;
        TextSlider.Value = s.TextSize;
        SmoothingBox.IsChecked = s.PenSmoothing;
        PressureBox.IsChecked = s.PenPressure;
        BackgroundCombo.SelectedItem = BackgroundCombo.Items.Cast<ComboBoxItem>().First(i => (BoardBackground)i.Tag == s.DefaultBackground);
        ZoomSlider.Value = s.WheelZoomStep;
        AutoSaveCombo.SelectedItem = AutoSaveCombo.Items.Cast<ComboBoxItem>().First(i => (int)i.Tag == s.AutoSaveMinutes);
        ConfirmClearBox.IsChecked = s.ConfirmClear;
        _loading = false;
        Refresh();
    }

    private AppSettings Read() => new AppSettings
    {
        PenThickness = PenSlider.Value,
        ShapeThickness = ShapeSlider.Value,
        TextSize = TextSlider.Value,
        PenSmoothing = SmoothingBox.IsChecked == true,
        PenPressure = PressureBox.IsChecked == true,
        DefaultBackground = BackgroundCombo.SelectedItem is ComboBoxItem { Tag: BoardBackground bg } ? bg : BoardBackground.Blanc,
        WheelZoomStep = Math.Round(ZoomSlider.Value, 2),
        AutoSaveMinutes = AutoSaveCombo.SelectedItem is ComboBoxItem { Tag: int m } ? m : 0,
        ConfirmClear = ConfirmClearBox.IsChecked == true
    }.Normalized();

    private void Any_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsInitialized) return;
        Refresh();
    }

    /// <summary>Met à jour les valeurs affichées et l'aperçu, puis applique au tableau.</summary>
    private void Refresh()
    {
        if (ZoomValue == null || PreviewCanvas == null || TextValue == null) return;
        var s = Read();
        PenValue.Text = $"{s.PenThickness:0.#} px";
        ShapeValue.Text = $"{s.ShapeThickness:0.#} px";
        TextValue.Text = $"{s.TextSize:0} px";
        ZoomValue.Text = $"+{(s.WheelZoomStep - 1) * 100:0} %";
        DrawPreview(s);
        _apply(s);
    }

    private void DrawPreview(AppSettings s)
    {
        PreviewCanvas.Children.Clear();
        var ink = (Brush)FindResource("IconBrush");

        // Trait à main levée (sinusoïde)
        var wave = new Polyline
        {
            Stroke = ink, StrokeThickness = s.PenThickness,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        };
        for (double x = 0; x <= 240; x += 4)
            wave.Points.Add(new Point(18 + x * 0.95, 23 + 9 * Math.Sin(x / 22)));
        PreviewCanvas.Children.Add(wave);

        // Forme (rectangle)
        var rect = new Rectangle
        {
            Width = 104, Height = 28, Stroke = (Brush)FindResource("AccentBrush"), StrokeThickness = s.ShapeThickness
        };
        Canvas.SetLeft(rect, 330);

        // Texte (taille réelle, plafonnée à la hauteur de l'aperçu)
        var sample = new System.Windows.Shapes.Path
        {
            Data = BoardText.BuildGeometry("Aa", Math.Min(s.TextSize, 34)), Fill = ink
        };
        Canvas.SetLeft(sample, 268);
        Canvas.SetTop(sample, Math.Max(0, 23 - Math.Min(s.TextSize, 34) * 0.7));
        PreviewCanvas.Children.Add(sample);
        Canvas.SetTop(rect, 9);
        PreviewCanvas.Children.Add(rect);
    }

    private void ConnectAssistant_Click(object sender, RoutedEventArgs e) =>
        new HelpWindow(HelpTab.Assistant) { Owner = this }.ShowDialog();

    private void Account_Click(object sender, RoutedEventArgs e)
    {
        if (!_cloudAccount.IsConnected)
        {
            var dialog = new AccountWindow(_cloudClient, _cloudAccount) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                RefreshAccountStatus();
            }
        }
        else
        {
            if (MessageBox.Show(this, "Voulez-vous fermer la session cloud ? Votre compte sera déconnecté.",
                    "GreenBoard", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                // Déconnecter : effacer le fichier de compte
                try { File.Delete(CloudAccountPath.Default); } catch { }
                _cloudAccount = CloudAccount.Disconnected();
                RefreshAccountStatus();
            }
        }
    }

    private void RefreshAccountStatus()
    {
        if (_cloudAccount.IsConnected)
        {
            AccountButton.Content = "Fermer la session";
            AccountStatus.Text = $"Connecté en tant que {_cloudAccount.Email} — {(_cloudClient.IsOffline ? "(serveur non configuré, mode hors-ligne)" : "")}";
            AccountStatus.Foreground = (Brush)FindResource("AccentBrush");
        }
        else
        {
            AccountButton.Content = "Se connecter";
            AccountStatus.Text = _cloudClient.IsOffline
                ? "Non connecté — serveur cloud non configuré (mode hors-ligne)."
                : "Non connecté — GreenBoard reste 100 % local.";
            AccountStatus.Foreground = (Brush)FindResource("IconBrush");
        }
    }

    private void Defaults_Click(object sender, RoutedEventArgs e) => Show(new AppSettings());

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Result = Read();
        _saved = true;
        DialogResult = true;
    }
}

