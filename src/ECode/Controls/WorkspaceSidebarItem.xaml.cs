using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ECode.ViewModels;

namespace ECode.Controls;

/// <summary>侧边栏单个项目项控件，支持拖拽排序和右键菜单</summary>
public partial class WorkspaceSidebarItem : UserControl
{
    public WorkspaceSidebarItem()
    {
        InitializeComponent();
    }

    private WorkspaceViewModel? Vm => DataContext as WorkspaceViewModel;
    private MainViewModel? MainVm => FindMainViewModel();

    private void Rename_Click(object sender, RoutedEventArgs e) => StartRename();

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (MainVm is { } main && Vm is { } ws)
        {
            main.DuplicateWorkspace(ws);
        }
    }

    private void CopyId_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is { } ws)
            Clipboard.SetText(ws.Workspace.Id);
    }

    private void NewSurface_Click(object sender, RoutedEventArgs e) => Vm?.CreateNewSurface();

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (MainVm is { } main && Vm is { } ws)
        {
            int idx = main.Workspaces.IndexOf(ws);
            if (idx > 0) main.MoveWorkspace(ws, idx - 1);
        }
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (MainVm is { } main && Vm is { } ws)
        {
            int idx = main.Workspaces.IndexOf(ws);
            if (idx >= 0 && idx < main.Workspaces.Count - 1)
                main.MoveWorkspace(ws, idx + 1);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (MainVm is { } main && Vm is { } ws)
            main.CloseWorkspace(ws);
    }

    private void StartRename()
    {
        NameDisplay.Visibility = Visibility.Collapsed;
        NameEditor.Visibility = Visibility.Visible;
        NameEditor.SelectAll();
        NameEditor.Focus();
    }

    private void FinishRename()
    {
        NameEditor.Visibility = Visibility.Collapsed;
        NameDisplay.Visibility = Visibility.Visible;
    }

    private void NameDisplay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            StartRename();
            e.Handled = true;
        }
    }

    private void NameEditor_LostFocus(object sender, RoutedEventArgs e) => FinishRename();

    private void NameEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Escape)
        {
            if (e.Key == Key.Escape && Vm != null)
                NameEditor.Text = Vm.Name; // revert
            FinishRename();
            e.Handled = true;
        }
    }

    private MainViewModel? FindMainViewModel()
    {
        var window = Window.GetWindow(this);
        return window?.DataContext as MainViewModel;
    }
}
