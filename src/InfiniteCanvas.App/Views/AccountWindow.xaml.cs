using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace InfiniteCanvas.App.Views;

/// <summary>
/// Fenêtre de création / connexion de compte GreenBoard Cloud.
/// Mode hors-ligne : aucune requête réseau, message d'information affiché.
/// </summary>
public partial class AccountWindow : Window, INotifyPropertyChanged
{
    private readonly CloudClient _client;
    private readonly CloudAccount _current;
    private string _statusMessage = "";
    private string _statusColor = "#6B7280";
    private bool _isLoggingIn;

    public AccountWindow(CloudClient client, CloudAccount current)
    {
        InitializeComponent();
        _client = client;
        _current = current;
        DataContext = this;

        if (_current.IsConnected)
        {
            StatusMessage = $"Déjà connecté en tant que {_current.Email}.";
            StatusColor = "#2E7D32";
        }
        else if (_client.IsOffline)
        {
            StatusMessage = "Serveur cloud non configuré : mode hors-ligne.";
            StatusColor = "#D97706";
        }
        else
        {
            StatusMessage = "Prêt à se connecter.";
            StatusColor = "#6B7280";
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public string StatusColor
    {
        get => _statusColor;
        set => SetField(ref _statusColor, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        if (_isLoggingIn) return;
        var email = EmailBox.Text?.Trim();
        var password = PasswordBox.Password;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            SetStatus("E-mail et mot de passe requis.", "#D93025");
            return;
        }

        _isLoggingIn = true;
        try
        {
            var result = await _client.LoginAsync(email, password, default);
            if (result?.IsOffline == true)
            {
                SetStatus(result.Error ?? "Serveur non joignable.", "#D97706");
                return;
            }
            if (result == null || result.AccessToken == null || result.User == null)
            {
                SetStatus("Identifiants incorrects ou serveur indisponible.", "#D93025");
                return;
            }

            // Mémoriser le compte (jeton chiffré avec DPAPI)
            var account = new CloudAccount
            {
                IsConnected = true,
                UserId = result.User.Id,
                Email = result.User.Email,
                DisplayName = result.User.DisplayName,
                EncryptedRefreshToken = result.RefreshToken != null ? DpapiHelper.Encrypt(result.RefreshToken) : null,
                ConnectedAt = DateTimeOffset.UtcNow
            };
            var json = DpapiHelper.Serialize(account);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CloudAccountPath.Default)!);
            System.IO.File.WriteAllText(CloudAccountPath.Default, json);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            SetStatus($"Erreur : {ex.Message}", "#D93025");
        }
        finally
        {
            _isLoggingIn = false;
        }
    }

    private async void Register_Click(object sender, RoutedEventArgs e)
    {
        var email = EmailBox.Text?.Trim();
        var password = PasswordBox.Password;
        var displayName = DisplayNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            SetStatus("E-mail et mot de passe requis.", "#D93025");
            return;
        }

        _isLoggingIn = true;
        try
        {
            var result = await _client.RegisterAsync(email, password, displayName, default);
            if (result?.IsOffline == true)
            {
                SetStatus(result.Error ?? "Serveur non joignable.", "#D97706");
                return;
            }
            if (result == null || result.AccessToken == null || result.User == null)
            {
                SetStatus("Impossible de créer le compte (e-mail peut-être déjà utilisé).", "#D93025");
                return;
            }

            SetStatus("Compte créé. Vous pouvez maintenant vous connecter.", "#2E7D32");
        }
        catch (Exception ex)
        {
            SetStatus($"Erreur : {ex.Message}", "#D93025");
        }
        finally
        {
            _isLoggingIn = false;
        }
    }

    private void SetStatus(string message, string color)
    {
        StatusMessage = message;
        StatusColor = color;
    }
}
