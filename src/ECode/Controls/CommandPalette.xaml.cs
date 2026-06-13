using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ECode.Controls;

/// <summary>命令面板项数据模型</summary>
public class PaletteItem
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Shortcut { get; set; }
    public string Icon { get; set; } = "\uE756";
    public string Category { get; set; } = "Commands";
    public string? SearchText { get; set; }
    public Action? Execute { get; set; }
}

/// <summary>命令面板控件，提供模糊搜索和快速执行应用命令</summary>
public partial class CommandPalette : UserControl
{
    private List<PaletteItem> _allItems = [];
    public ObservableCollection<PaletteItem> FilteredItems { get; } = [];

    public event Action? PaletteClosed;
    public event Action<PaletteItem>? ItemExecuted;

    public CommandPalette()
    {
        InitializeComponent();
        ResultsList.ItemsSource = FilteredItems;
    }

    public void Show(List<PaletteItem> items)
    {
        _allItems = items;
        SearchInput.Text = string.Empty;
        Filter(string.Empty);
        Visibility = Visibility.Visible;
        SearchInput.Focus();
    }

    public void Hide()
    {
        Visibility = Visibility.Collapsed;
        PaletteClosed?.Invoke();
    }

    public void Filter(string query)
    {
        FilteredItems.Clear();

        var matches = string.IsNullOrWhiteSpace(query)
            ? _allItems
            : _allItems.Where(item =>
                item.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (item.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                item.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (item.SearchText?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));

        foreach (var item in matches.Take(20))
            FilteredItems.Add(item);

        EmptyText.Visibility = FilteredItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ResultsList.Visibility = FilteredItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (FilteredItems.Count > 0)
            ResultsList.SelectedIndex = 0;
    }

    private void ExecuteSelected()
    {
        if (ResultsList.SelectedItem is PaletteItem item)
            ExecuteItem(item);
    }

    private void ExecuteItem(PaletteItem item)
    {
        Hide();
        item.Execute?.Invoke();
        ItemExecuted?.Invoke(item);
    }

    private void SearchInput_TextChanged(object sender, TextChangedEventArgs e)
        => Filter(SearchInput.Text);

    private void SearchInput_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Hide();
                e.Handled = true;
                break;
            case Key.Enter:
                ExecuteSelected();
                e.Handled = true;
                break;
            case Key.Down:
                if (ResultsList.SelectedIndex < FilteredItems.Count - 1)
                    ResultsList.SelectedIndex++;
                ResultsList.ScrollIntoView(ResultsList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up:
                if (ResultsList.SelectedIndex > 0)
                    ResultsList.SelectedIndex--;
                ResultsList.ScrollIntoView(ResultsList.SelectedItem);
                e.Handled = true;
                break;
        }
    }

    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => ExecuteSelected();

    private void ResultsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;

        var item = ItemsControl.ContainerFromElement(ResultsList, source) as ListBoxItem;
        if (item?.DataContext is PaletteItem paletteItem)
        {
            ExecuteItem(paletteItem);
            e.Handled = true;
        }
    }
}
