using System.Windows;

namespace InfiniteCanvas.App.Views;

/// <summary>Petite boîte de saisie de nom pour l'enregistrement cloud.</summary>
public partial class NameInputDialog : Window
{
    public string DialogName => NameBox.Text;

    public NameInputDialog(string defaultName = "")
    {
        InitializeComponent();
        NameBox.Text = defaultName;
        NameBox.SelectAll();
        NameBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
