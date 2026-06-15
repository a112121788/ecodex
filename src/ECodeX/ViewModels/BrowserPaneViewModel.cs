using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ECodeX.ViewModels;

/// <summary>
/// Live browser pane state shared by BrowserControl and future CLI/browser automation.
/// </summary>
public partial class BrowserPaneViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private string _url = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private string _title = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _canGoBack;

    [ObservableProperty]
    private bool _canGoForward;

    [ObservableProperty]
    private bool _isWebViewAvailable = true;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private int _navigationVersion;

    public ObservableCollection<string> History { get; } = [];

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title)
        ? (string.IsNullOrWhiteSpace(Url) ? "Browser" : Url)
        : Title;

    public void BeginNavigation(string url)
    {
        Url = NormalizeUrl(url);
        IsLoading = true;
        ErrorMessage = null;
        AddHistory(Url);
    }

    public void CompleteNavigation(
        string? url,
        string? title,
        bool canGoBack,
        bool canGoForward,
        bool success,
        string? errorMessage = null)
    {
        UpdateNavigationState(url, title, canGoBack, canGoForward);
        IsLoading = false;
        ErrorMessage = success ? null : errorMessage ?? "Navigation failed.";
    }

    public void UpdateNavigationState(string? url, string? title, bool canGoBack, bool canGoForward)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            Url = url;
            AddHistory(url);
        }

        if (title != null)
            Title = title;

        CanGoBack = canGoBack;
        CanGoForward = canGoForward;
        NavigationVersion++;
    }

    public void SetWebViewUnavailable(string message)
    {
        IsWebViewAvailable = false;
        IsLoading = false;
        ErrorMessage = message;
    }

    public static string NormalizeUrl(string url)
    {
        var trimmed = (url ?? "").Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return "";

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return "https://" + trimmed;
    }

    private void AddHistory(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        if (History.Count > 0 && string.Equals(History[^1], url, StringComparison.Ordinal))
            return;

        History.Add(url);
    }
}
