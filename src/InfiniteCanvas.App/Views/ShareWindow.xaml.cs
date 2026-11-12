using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace InfiniteCanvas.App.Views;

/// <summary>Fenêtre « Partager par e-mail » : destinataires, objet, message, pages et nom du PDF.</summary>
public partial class ShareWindow : Window
{
    private readonly List<string> _recent;

    public ShareRequest? Result { get; private set; }

    public ShareWindow(string boardName, string currentPageTitle, int pageCount, List<string> recentRecipients)
    {
        InitializeComponent();
        _recent = recentRecipients;

        SubjectBox.Text = $"Tableau : {boardName}";
        BodyBox.Text = $"Bonjour,\r\n\r\nVous trouverez ci-joint le tableau « {boardName} » au format PDF.\r\n\r\nBonne journée.";
        FileNameBox.Text = MailShare.SafeFileName(boardName);

        AllPagesRadio.Content = pageCount > 1 ? $"Toutes les pages ({pageCount})" : "Le tableau";
        CurrentPageRadio.Content = $"Page actuelle « {currentPageTitle} »";
        CurrentPageRadio.Visibility = pageCount > 1 ? Visibility.Visible : Visibility.Collapsed;

        if (_recent.Count > 0)
        {
            RecentText.Text = $"Reprendre les derniers destinataires ({string.Join(", ", _recent.Take(3))}{(_recent.Count > 3 ? "…" : "")})";
            RecentLink.Visibility = Visibility.Visible;
        }

        Loaded += (_, _) => RecipientsBox.Focus();
    }

    private void UseRecent_Click(object sender, RoutedEventArgs e)
    {
        RecipientsBox.Text = string.Join("; ", _recent);
        RecipientsBox.CaretIndex = RecipientsBox.Text.Length;
        RecipientsBox.Focus();
    }

    /// <summary>Affiche chaque adresse en pastille : verte si valide, rouge sinon.</summary>
    private void RecipientsBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var (valid, invalid) = MailShare.ParseRecipients(RecipientsBox.Text);
        RecipientChips.Children.Clear();
        foreach (var address in valid) RecipientChips.Children.Add(Chip(address, ok: true));
        foreach (var address in invalid) RecipientChips.Children.Add(Chip(address, ok: false));

        SendButton.IsEnabled = valid.Count > 0 && invalid.Count == 0;
        SendButton.ToolTip = invalid.Count > 0 ? "Corrigez les adresses en rouge." : null;
    }

    private static Border Chip(string text, bool ok) => new()
    {
        Background = new SolidColorBrush(ok ? Color.FromRgb(0xE3, 0xF2, 0xE5) : Color.FromRgb(0xFD, 0xE8, 0xE8)),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(9, 2, 9, 3),
        Margin = new Thickness(0, 0, 6, 4),
        ToolTip = ok ? null : "Adresse invalide",
        Child = new TextBlock
        {
            Text = ok ? text : $"⚠ {text}",
            FontSize = 12,
            Foreground = new SolidColorBrush(ok ? Color.FromRgb(0x1B, 0x5E, 0x20) : Color.FromRgb(0xB7, 0x1C, 0x1C))
        }
    };

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        var (valid, invalid) = MailShare.ParseRecipients(RecipientsBox.Text);
        if (valid.Count == 0 || invalid.Count > 0) return;

        Result = new ShareRequest(
            valid,
            SubjectBox.Text.Trim(),
            BodyBox.Text,
            AllPages: AllPagesRadio.IsChecked == true,
            FileName: MailShare.SafeFileName(FileNameBox.Text));
        DialogResult = true;
    }
}
