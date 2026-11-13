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
