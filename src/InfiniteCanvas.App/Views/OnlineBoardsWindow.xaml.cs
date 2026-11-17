using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace InfiniteCanvas.App.Views;

/// <summary>
/// Fenêtre « Mes tableaux en ligne » : liste, ouvre, renomme et supprime les documents
/// stockés dans l'espace GreenBoard Cloud de l'utilisateur connecté.
/// </summary>
public partial class OnlineBoardsWindow : Window
{
    private readonly CloudClient _client;
    private readonly CloudAccount _account;
    private readonly Action<byte[], string> _onOpenDocument;
    private string _accessToken = "";

    public OnlineBoardsWindow(CloudClient client, CloudAccount account, Action<byte[], string> onOpenDocument)
    {
        InitializeComponent();
        _client = client;
        _account = account;
        _onOpenDocument = onOpenDocument;

        Title = $"Mes tableaux en ligne — {account.Email}";
        Loaded += async (_, _) => await RefreshAsync();
    }

    /// <summary>Définit le jeton d'accès courant (à rafraîchir si nécessaire).</summary>
    public void SetAccessToken(string token) => _accessToken = token;

    private async Task RefreshAsync()
    {
        DocumentsList.Items.Clear();
        EmptyMessage.Visibility = Visibility.Collapsed;

        try
        {
            var docs = await _client.ListDocumentsAsync(_accessToken, default);
            if (docs == null || docs.Count == 0)
            {
                EmptyMessage.Visibility = Visibility.Visible;
                return;
            }

            foreach (var doc in docs)
                DocumentsList.Items.Add(doc);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Impossible de charger la liste des tableaux :\n{ex.Message}",
                "GreenBoard", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CloudDocument doc }) return;
        try
        {
            var content = await _client.DownloadDocumentAsync(_accessToken, doc.Id, default);
            if (content == null || content.Content.Length == 0)
            {
                MessageBox.Show(this, "Le tableau est vide ou n'a pas pu être téléchargé.",
                    "GreenBoard", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _onOpenDocument(content.Content, content.Title);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Impossible d'ouvrir le tableau :\n{ex.Message}",
                "GreenBoard", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CloudDocument doc }) return;
        if (MessageBox.Show(this, $"Supprimer le tableau « {doc.Title} » ? Cette action est irréversible.",
                "GreenBoard", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            var ok = await _client.DeleteDocumentAsync(_accessToken, doc.Id, default);
            if (ok)
            {
                DocumentsList.Items.Remove(doc);
                if (DocumentsList.Items.Count == 0)
                    EmptyMessage.Visibility = Visibility.Visible;
            }
            else
            {
                MessageBox.Show(this, "La suppression a échoué.", "GreenBoard",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Erreur : {ex.Message}", "GreenBoard",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false; // signale la déconnexion
        Close();
    }
}
