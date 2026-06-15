using ECodeX.Core.Models;

namespace ECodeX.Services;

public sealed record ManagedWindowInfo(
    string Id,
    ShortRef Ref,
    string Title,
    bool IsCurrent,
    DateTime CreatedAtUtc);

public class WindowManagerService<TWindow>
    where TWindow : class
{
    private sealed record WindowEntry(
        string Id,
        TWindow Window,
        string Title,
        DateTime CreatedAtUtc);

    private readonly object _gate = new();
    private readonly Dictionary<string, WindowEntry> _windows = new(StringComparer.Ordinal);
    private readonly List<string> _windowOrder = [];
    private string? _currentWindowId;

    public event Action? WindowsChanged;

    public int Count
    {
        get
        {
            lock (_gate) return _windowOrder.Count;
        }
    }

    public string? CurrentWindowId
    {
        get
        {
            lock (_gate) return _currentWindowId;
        }
    }

    public ManagedWindowInfo RegisterWindow(TWindow window, string? title = null, bool makeCurrent = true)
    {
        ArgumentNullException.ThrowIfNull(window);

        lock (_gate)
        {
            var existing = _windows.FirstOrDefault(item => ReferenceEquals(item.Value.Window, window));
            if (!string.IsNullOrEmpty(existing.Key))
            {
                var currentEntry = existing.Value;
                var updated = currentEntry with { Title = NormalizeTitle(title, currentEntry.Title) };
                _windows[existing.Key] = updated;
                if (makeCurrent)
                    _currentWindowId = existing.Key;

                NotifyChanged();
                return ToInfo(updated, _windowOrder.IndexOf(existing.Key), existing.Key == _currentWindowId);
            }

            var id = Guid.NewGuid().ToString();
            var entry = new WindowEntry(
                Id: id,
                Window: window,
                Title: NormalizeTitle(title, $"Window {_windowOrder.Count + 1}"),
                CreatedAtUtc: DateTime.UtcNow);
            _windows[id] = entry;
            _windowOrder.Add(id);
            if (makeCurrent || _currentWindowId == null)
                _currentWindowId = id;

            NotifyChanged();
            return ToInfo(entry, _windowOrder.Count - 1, id == _currentWindowId);
        }
    }

    public ManagedWindowInfo CreateWindow(Func<TWindow> factory, Action<TWindow>? show = null, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var window = factory();
        var info = RegisterWindow(window, title, makeCurrent: true);
        show?.Invoke(window);
        return info;
    }

    public bool UnregisterWindow(string? windowId)
    {
        if (string.IsNullOrWhiteSpace(windowId))
            return false;

        lock (_gate)
        {
            if (!_windows.Remove(windowId))
                return false;

            _windowOrder.Remove(windowId);
            if (string.Equals(_currentWindowId, windowId, StringComparison.Ordinal))
                _currentWindowId = _windowOrder.LastOrDefault();

            NotifyChanged();
            return true;
        }
    }

    public bool CloseWindow(string windowId, Action<TWindow> close)
    {
        ArgumentNullException.ThrowIfNull(close);

        TWindow window;
        lock (_gate)
        {
            if (!_windows.TryGetValue(windowId, out var entry))
                return false;

            window = entry.Window;
        }

        close(window);
        UnregisterWindow(windowId);
        return true;
    }

    public bool FocusWindow(string windowId, Action<TWindow>? focus = null)
    {
        TWindow window;
        lock (_gate)
        {
            if (!_windows.TryGetValue(windowId, out var entry))
                return false;

            _currentWindowId = windowId;
            window = entry.Window;
            NotifyChanged();
        }

        focus?.Invoke(window);
        return true;
    }

    public bool TryGetWindow(string windowId, out TWindow window)
    {
        lock (_gate)
        {
            if (_windows.TryGetValue(windowId, out var entry))
            {
                window = entry.Window;
                return true;
            }
        }

        window = null!;
        return false;
    }

    public IReadOnlyList<ManagedWindowInfo> ListWindows()
    {
        lock (_gate)
        {
            return _windowOrder
                .Select((id, index) => ToInfo(_windows[id], index, id == _currentWindowId))
                .ToList();
        }
    }

    private static string NormalizeTitle(string? title, string fallback)
    {
        return string.IsNullOrWhiteSpace(title) ? fallback : title.Trim();
    }

    private static ManagedWindowInfo ToInfo(WindowEntry entry, int zeroBasedIndex, bool isCurrent)
    {
        return new ManagedWindowInfo(
            Id: entry.Id,
            Ref: new ShortRef(ShortRefKind.Window, zeroBasedIndex + 1),
            Title: entry.Title,
            IsCurrent: isCurrent,
            CreatedAtUtc: entry.CreatedAtUtc);
    }

    private void NotifyChanged()
    {
        WindowsChanged?.Invoke();
    }
}

public sealed class WindowManagerService : WindowManagerService<object>
{
}
