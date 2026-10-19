// SPDX-License-Identifier: GPL-3.0-or-later
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Velora.App.Viewer;

namespace Velora.App;

public partial class MainWindow
{
    private MenuItem? _menuSidebar, _menuTwoPages, _menuCover, _menuNote, _menuInk;
    private double _menuFullWidth, _menuCompactWidth;

    private MenuItem Item(RoutedUICommand command, bool checkable = false, string? gesture = null)
    {
        var mi = new MenuItem { Command = command, CommandTarget = this, IsCheckable = checkable };
        if (gesture != null) mi.InputGestureText = gesture;
        return mi;
    }

    private static MenuItem Sub(string header, params object[] children)
    {
        var mi = new MenuItem { Header = header };
        foreach (var c in children) mi.Items.Add(c);
        return mi;
    }

    /// <summary>Crea de nuevo todos los menús (cada vez, elementos nuevos: WPF no permite pasar un elemento de un menú a otro).</summary>
    private List<MenuItem> CreateMenuItems()
    {
        _menuSidebar = Item(AppCommands.Sidebar, true);
        _menuTwoPages = Item(AppCommands.TwoPages, true);
        _menuCover = Item(AppCommands.CoverAlone, true);
        _menuNote = Item(AppCommands.NoteToolMenu, true);
        _menuInk = Item(AppCommands.InkToolMenu, true);
        return
        [
            Sub("_Archivo",
                Item(AppCommands.NewWindow), Item(AppCommands.Open), Item(AppCommands.OpenFolder), new Separator(),
                Item(AppCommands.Save), Item(AppCommands.SaveCopy), new Separator(),
                Item(AppCommands.Print), Item(AppCommands.Properties), new Separator(),
                Item(AppCommands.CloseDocument), Item(AppCommands.Exit, gesture: "Alt+F4")),
            Sub("_Ver",
                _menuSidebar, Item(AppCommands.FullScreen), Item(AppCommands.Presentation), new Separator(),
                Item(AppCommands.ZoomIn), Item(AppCommands.ZoomOut), Item(AppCommands.FitWidth), Item(AppCommands.FitPage), Item(AppCommands.Zoom100), new Separator(),
                Item(AppCommands.RotateLeft), Item(AppCommands.RotateRight), _menuTwoPages, _menuCover),
            Sub("A_notar",
                Item(AppCommands.Highlight), Item(AppCommands.Underline), Item(AppCommands.Squiggly), Item(AppCommands.Strike), new Separator(),
                _menuNote, _menuInk, Item(AppCommands.ColorMenu)),
            Sub("_Ir",
                Item(AppCommands.PreviousPage), Item(AppCommands.NextPage), Item(AppCommands.GoToPage), new Separator(),
                Item(AppCommands.Find), Item(AppCommands.FindNext), Item(AppCommands.FindPrevious), new Separator(),
                Item(AppCommands.Back), Item(AppCommands.Forward)),
            Sub("Ay_uda",
                Item(AppCommands.Manual), Item(AppCommands.CheckUpdates), new Separator(), Item(AppCommands.About)),
        ];
    }

    /// <summary>Coloca el menú completo en la barra, o recogido en un solo botón (☰) cuando falta espacio.</summary>
    private void BuildMenus(bool compact)
    {
        MainMenu.Items.Clear();
        MenuHamburger.Items.Clear();
        foreach (var item in CreateMenuItems()) (compact ? (ItemsControl)MenuHamburger : MainMenu).Items.Add(item);
        MainMenu.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactMenu.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        _menuCollapsed = compact;
    }

    private void SetMenuCollapsed(bool collapsed)
    {
        if (collapsed == _menuCollapsed && (MainMenu.Items.Count > 0 || MenuHamburger.Items.Count > 0)) return;
        BuildMenus(collapsed);
    }

    /// <summary>Anchos de la zona izquierda con el menú completo y con el menú recogido (se miden una sola vez).</summary>
    private (double Full, double Compact) MenuWidths()
    {
        if (_menuFullWidth <= 0)
        {
            BuildMenus(false);
            _menuFullWidth = Wide(HeaderLeft);
            BuildMenus(true);
            _menuCompactWidth = Wide(HeaderLeft);
        }

        return (_menuFullWidth, _menuCompactWidth);
    }

    private void OnMenuOpened(object sender, RoutedEventArgs e)
    {
        if (_menuSidebar != null) _menuSidebar.IsChecked = _settings.SidebarVisible && HasDoc;
        if (_menuTwoPages != null) _menuTwoPages.IsChecked = _twoPages;
        if (_menuCover != null) _menuCover.IsChecked = _coverAlone;
        if (_menuNote != null) _menuNote.IsChecked = View.Tool == ViewTool.Note;
        if (_menuInk != null) _menuInk.IsChecked = View.Tool == ViewTool.Ink;
        CommandManager.InvalidateRequerySuggested();
    }

    private void OpenManual()
    {
        string file = Path.Combine(AppContext.BaseDirectory, "MANUAL.md");
        try
        {
            string target = File.Exists(file) ? file : UpdateService.RepositoryUrl + "/blob/main/docs/MANUAL.md";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }
}
