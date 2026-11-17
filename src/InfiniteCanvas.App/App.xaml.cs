using System.Diagnostics;
using System.Windows;

namespace InfiniteCanvas.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    /// <summary>Durée minimale d'affichage de l'écran de démarrage (lisible même sur un PC rapide).</summary>
    private static readonly TimeSpan MinSplash = TimeSpan.FromMilliseconds(1500);

    private SplashScreen? _splash;
    private readonly Stopwatch _sinceStart = Stopwatch.StartNew();

    // Pas d'InitializeComponent ici : App.g.cs (Main) l'appelle déjà
    protected override void OnStartup(StartupEventArgs e)
    {
        _splash = new SplashScreen("Assets/Splash.png");
        _splash.Show(autoClose: false, topMost: true);
        base.OnStartup(e);

        // Vérification de la licence avant d'ouvrir la fenêtre principale
        var (allowed, message) = LicenseManager.Check();

        if (!allowed)
        {
            // Démo expirée : forcer la saisie d'une clé ou quitter
            _splash?.Close(TimeSpan.FromMilliseconds(0));
            var dlg = new Views.LicenceKeyWindow();
            if (dlg.ShowDialog() == true)
            {
                // Clé valide saisie : ouvrir la fenêtre principale
                var main = new MainWindow();
                MainWindow = main;
                main.Show();
            }
            else
            {
                // Utilisateur a annulé : quitter l'application
                Shutdown();
            }
            return;
        }

        // Clé valide ou démo encore valide : ouvrir normalement
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();

        // Afficher un rappel de démo si nécessaire
        if (!string.IsNullOrEmpty(message))
        {
            _ = System.Windows.MessageBox.Show(mainWindow, message, "GreenBoard — version d'essai",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        _splash?.Close(TimeSpan.FromMilliseconds(350));
    }

    /// <summary>Appelé par la fenêtre principale une fois affichée : fondu de l'écran de démarrage.</summary>
    public async void HideSplash()
    {
        if (_splash == null) return;
        var splash = _splash;
        _splash = null;

        var remaining = MinSplash - _sinceStart.Elapsed;
        if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
        splash.Close(TimeSpan.FromMilliseconds(350));
    }
}
