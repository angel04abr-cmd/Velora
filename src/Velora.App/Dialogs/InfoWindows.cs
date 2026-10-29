// SPDX-License-Identifier: GPL-3.0-or-later
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Velora.Updates;

namespace Velora.App;

/// <summary>Ventana con un texto con formato (Markdown sencillo): manual de uso, licencias y créditos. Se puede cambiar de tamaño.</summary>
internal sealed class DocumentWindow : Window
{
    public DocumentWindow(string title, string markdown, Window? owner)
    {
        Title = title;
        Width = 760;
        Height = 700;
        MinWidth = 420;
        MinHeight = 300;
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/app.png"));
        ShowInTaskbar = true;
        if (owner is { IsVisible: true })
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var viewer = new FlowDocumentScrollViewer
        {
            Document = RichText.Build(markdown),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsToolBarVisible = false,
            Margin = new Thickness(18, 12, 12, 0),
            Focusable = true,
        };
        var close = new Button { Content = "Cerrar", MinWidth = 88, Padding = new Thickness(14, 6, 14, 6), IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 18, 14) };
        close.Click += (_, _) => Close();
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(close, 1);
        grid.Children.Add(viewer);
        grid.Children.Add(close);
        Content = grid;
        Loaded += (_, _) => viewer.Focus();
    }
}

/// <summary>
/// Aviso de versión nueva: versión, novedades con formato y botones. Al pulsar «Actualizar ahora» la descarga se sigue en esta misma ventana.
/// </summary>
internal sealed class UpdateDialog : Window
{
    public enum Choice { Later, Skip, Updated }

    private readonly Button _now, _later, _skip;
    private readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 1, Height = 6, Margin = new Thickness(0, 14, 0, 4), Visibility = Visibility.Collapsed };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.85, Visibility = Visibility.Collapsed };
    private readonly Func<IProgress<double>, Action<string>, Task> _install;
    private bool _busy;

    public Choice Result { get; private set; } = Choice.Later;

    public UpdateDialog(UpdateInfo info, string currentVersion, bool canInstall, Func<IProgress<double>, Action<string>, Task> install, Window? owner)
    {
        _install = install;
        Title = "Actualización de Velora";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 720;
        MinHeight = 260;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/app.png"));
        if (owner is { IsVisible: true })
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/app.png")), Width = 52, Height = 52, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Top };
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = "Hay una versión nueva de Velora", FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        titles.Children.Add(new TextBlock { Text = $"Versión {info.Version}  ·  tienes la {currentVersion}  ·  descarga de {T.FileSize(info.MsiSize)}", Opacity = 0.7, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(titles, 1);
        head.Children.Add(icon);
        head.Children.Add(titles);

        var notes = new FlowDocumentScrollViewer
        {
            Document = RichText.Build(CleanNotes(info.Notes), 13.5, compact: true),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsToolBarVisible = false,
            MaxHeight = 330,
            Margin = new Thickness(0, 6, 0, 0),
            Focusable = false,
        };
        var notesBox = new Border { Padding = new Thickness(14, 10, 8, 4), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 16, 0, 0), Child = notes };
        notesBox.SetResourceReference(Border.BackgroundProperty, "ControlFillColorSecondaryBrush");

        _now = new Button { Content = canInstall ? "Actualizar ahora" : "Abrir la página de descarga", Padding = new Thickness(16, 7, 16, 7), IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        _later = new Button { Content = "Más tarde", Padding = new Thickness(16, 7, 16, 7), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        _skip = new Button { Content = "Omitir esta versión", Padding = new Thickness(16, 7, 16, 7) };
        _now.Click += async (_, _) =>
        {
            if (!canInstall)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(info.PageUrl) { UseShellExecute = true });
                }
                catch
                {
                    // sin navegador
                }

                Close();
                return;
            }

            await RunInstallAsync();
        };
        _later.Click += (_, _) => Close();
        _skip.Click += (_, _) =>
        {
            Result = Choice.Skip;
            Close();
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0), Children = { _now, _later, _skip } };

        var panel = new StackPanel { Margin = new Thickness(26, 24, 26, 22) };
        panel.Children.Add(head);
        panel.Children.Add(new TextBlock { Text = "Novedades", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 0) });
        panel.Children.Add(notesBox);
        panel.Children.Add(_bar);
        panel.Children.Add(_status);
        panel.Children.Add(buttons);
        Content = panel;
        Closing += (_, e) => e.Cancel = _busy;
    }

    /// <summary>Quita lo que no aporta al usuario en las notas (la línea de título con la fecha, si viene).</summary>
    private static string CleanNotes(string notes)
    {
        var lines = notes.Replace("\r\n", "\n").Split('\n').ToList();
        while (lines.Count > 0 && (lines[0].Trim().Length == 0 || lines[0].TrimStart().StartsWith("## [", StringComparison.Ordinal))) lines.RemoveAt(0);
        string text = string.Join("\n", lines).Trim();
        return text.Length > 0 ? text : "Mejoras y correcciones.";
    }

    private async Task RunInstallAsync()
    {
        _busy = true;
        _now.IsEnabled = _later.IsEnabled = _skip.IsEnabled = false;
        _bar.Visibility = _status.Visibility = Visibility.Visible;
        _status.Text = "Preparando la descarga…";
        try
        {
            await _install(new Progress<double>(p => _bar.Value = p), s => _status.Text = s);
            Result = Choice.Updated;
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            _status.Text = "No se pudo actualizar; se sigue usando la versión actual.\n" + ex.Message;
            _status.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
            _bar.Visibility = Visibility.Collapsed;
            _now.IsEnabled = _later.IsEnabled = _skip.IsEnabled = true;
            _busy = false;
        }
    }
}
