using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace InfiniteCanvas.App.Views;

/// <summary>Fenêtre de saisie de la clé de licence au premier lancement.</summary>
public partial class LicenceKeyWindow : Window
{
    public string Key => KeyBox.Text.Trim().ToUpperInvariant();

    public LicenceKeyWindow()
    {
        InitializeComponent();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (LicenseManager.IsValidKey(Key))
        {
            DialogResult = true;
            Close();
        }
        else
        {
            MessageBox.Show(this, "Clé de licence invalide. Vérifiez le format (GB-XXXXX-XXXXX-XXXXX-XXXXX).",
                "GreenBoard", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Demo_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void KeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (KeyBox.Text == "GB----- ----- ----- -----")
        {
            KeyBox.Text = "";
            KeyBox.Foreground = (Brush)FindResource("IconBrush");
        }
    }

    private void KeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(KeyBox.Text))
        {
            KeyBox.Text = "GB----- ----- ----- -----";
            KeyBox.Foreground = Brushes.Gray;
        }
    }

    /// <summary>Autorise uniquement les caractères de l'alphabet de licence et les tirets.</summary>
    private void KeyBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var c = e.Text.ToUpperInvariant();
        if (c == "-" || LicenceAlphabet.Chars.Contains(c))
        {
            e.Handled = false;
        }
        else
        {
            e.Handled = true;
        }
    }
}
