// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Velora.Pdf;

namespace Velora.App;

/// <summary>Base de los cuadros de diálogo pequeños: centrados sobre la ventana, sin barra de herramientas.</summary>
internal abstract class DialogBase : Window
{
    protected DialogBase(string title)
    {
        Title = title;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SizeToContent = SizeToContent.WidthAndHeight;
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/app.png"));
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
    }

    protected static Button MakeButton(string text, bool isDefault = false, bool isCancel = false) => new()
    {
        Content = text,
        MinWidth = 88,
        Padding = new Thickness(14, 6, 14, 6),
        Margin = new Thickness(8, 0, 0, 0),
        IsDefault = isDefault,
        IsCancel = isCancel,
    };
}

internal sealed class PasswordDialog : DialogBase
{
    private readonly PasswordBox _box = new() { MinWidth = 320, Margin = new Thickness(0, 12, 0, 0) };

    internal PasswordDialog(string fileName, bool wrong) : base("Documento protegido")
    {
        var ok = MakeButton("Abrir", isDefault: true);
        var cancel = MakeButton("Cancelar", isCancel: true);
        ok.Click += (_, _) => DialogResult = true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = string.Format(T.PasswordNeeded, fileName), TextWrapping = TextWrapping.Wrap, MaxWidth = 380 });
        panel.Children.Add(_box);
        if (wrong)
            panel.Children.Add(new TextBlock { Text = T.PasswordWrong, Foreground = Brushes.IndianRed, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 380 });
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => _box.Focus();
    }

    public static string? Ask(Window owner, string fileName, bool wrong)
    {
        var d = new PasswordDialog(fileName, wrong) { Owner = owner };
        return d.ShowDialog() == true ? d._box.Password : null;
    }
}

internal sealed class PropertiesDialog : DialogBase
{
    public PropertiesDialog(DocumentProperties p) : base("Propiedades del documento")
    {
        var grid = new Grid { Margin = new Thickness(24, 20, 24, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 340, MaxWidth = 440 });
        int row = 0;

        void Add(string label, string value)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new TextBlock { Text = label, Opacity = 0.7, Margin = new Thickness(0, 4, 18, 4), VerticalAlignment = VerticalAlignment.Top };
            var v = new TextBox
            {
                Text = string.IsNullOrWhiteSpace(value) ? "—" : value,
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 1),
                Padding = new Thickness(0, 3, 0, 3),
            };
            Grid.SetRow(l, row);
            Grid.SetRow(v, row);
            Grid.SetColumn(v, 1);
            grid.Children.Add(l);
            grid.Children.Add(v);
            row++;
        }

        string Date(DateTime? d) => d?.ToString("d 'de' MMMM 'de' yyyy, HH:mm", System.Globalization.CultureInfo.GetCultureInfo("es-ES")) ?? "";
        Add("Archivo", p.FileName);
        Add("Ubicación", Path.GetDirectoryName(p.FullPath) ?? "");
        Add("Tamaño del archivo", T.FileSize(p.FileSize));
        Add("Título", p.Title);
        Add("Autor", p.Author);
        Add("Asunto", p.Subject);
        Add("Palabras clave", p.Keywords);
        Add("Creado con", p.Creator);
        Add("Convertido con", p.Producer);
        Add("Fecha de creación", Date(p.Created));
        Add("Fecha de modificación", Date(p.Modified));
        Add("Versión de PDF", p.PdfVersion);
        Add("Páginas", p.PageCount.ToString("N0"));
        Add("Tamaño de página", T.PageSizeText(p.FirstPageSize));
        Add("Protegido", p.Protected ? "Sí" : "No");
        if (p.Protected)
            Add("Permisos", $"Imprimir: {(p.CanPrint ? "sí" : "no")} · Copiar texto: {(p.CanCopy ? "sí" : "no")} · Modificar: {(p.CanModify ? "sí" : "no")}");
        Add("Firmas digitales", p.Signatures == 0 ? "Ninguna" : p.Signatures.ToString());

        var close = MakeButton("Cerrar", isDefault: true, isCancel: true);
        close.Click += (_, _) => Close();
        var panel = new StackPanel();
        panel.Children.Add(grid);
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(24, 8, 24, 18), Children = { close } });
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 640, Content = panel };
    }
}

internal sealed class AboutDialog : DialogBase
{
    public AboutDialog() : base("Acerca de Velora")
    {
        string version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "1.0.0";
        var panel = new StackPanel { Margin = new Thickness(28, 24, 28, 20), Width = 440 };
        panel.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/app.png")), Width = 72, Height = 72, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = "Velora", FontSize = 26, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(new TextBlock { Text = $"Velora PDF · Versión {version}", Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = T.Subtitle, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 14) });
        panel.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Opacity = 0.85,
            Margin = new Thickness(0, 0, 0, 18),
            Text = "Software libre bajo la Licencia Pública General de GNU, versión 3 o posterior. Se distribuye sin ninguna garantía.",
        });

        var licenses = MakeButton("Licencias y créditos");
        licenses.Click += (_, _) => ShowCredits(this);
        var close = MakeButton("Cerrar", isDefault: true, isCancel: true);
        close.Click += (_, _) => Close();
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Children = { licenses, close } });
        Content = panel;
    }

    /// <summary>Abre los avisos de terceros y créditos (el archivo que se instala junto al programa) en una ventana con formato.</summary>
    internal static void ShowCredits(Window owner)
    {
        string file = Path.Combine(AppContext.BaseDirectory, "AVISOS-DE-TERCEROS.md");
        string text;
        try
        {
            text = File.Exists(file) ? File.ReadAllText(file) : Fallback;
        }
        catch (IOException)
        {
            text = Fallback;
        }

        new DocumentWindow("Licencias y créditos", text, owner).Show();
    }

    private const string Fallback = """
        # Licencias y créditos

        Velora es software libre bajo la **Licencia Pública General de GNU, versión 3 o posterior (GPL-3.0-or-later)**. Se distribuye sin ninguna garantía.

        - La arquitectura de visor rápido y la terminología en español se inspiran en **Papers**, el visor de documentos de GNOME (GPL-2.0-or-later). Velora es un programa independiente y no está afiliado a GNOME.
        - Motor de PDF: **PDFium** (licencia BSD), con binarios de pdfium-binaries de Benoît Blanchon.
        - Plataforma: **.NET** y **WPF** (MIT, Microsoft).
        """;
}

/// <summary>Cuadro de mensaje con botones en español (los de Windows siguen el idioma del sistema).</summary>
internal sealed class MessageDialog : DialogBase
{
    public enum Kind { Info, Warning, Question }

    private MessageDialog(string message, Kind kind, string[] buttons, int defaultIndex, int cancelIndex) : base(T.AppName)
    {
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 440, VerticalAlignment = VerticalAlignment.Center };
        string glyph = kind switch { Kind.Warning => "\uE7BA", Kind.Question => "\uE897", _ => "\uE946" };
        var icon = new TextBlock
        {
            Text = glyph,
            FontFamily = (FontFamily)Application.Current.FindResource("IconFont"),
            FontSize = 30,
            Margin = new Thickness(0, 0, 18, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Foreground = kind == Kind.Warning ? Brushes.Goldenrod : (Brush)Application.Current.FindResource("AccentFillColorDefaultBrush"),
        };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(icon);
        head.Children.Add(text);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;
            var b = MakeButton(buttons[i], isDefault: i == defaultIndex, isCancel: i == cancelIndex);
            b.Click += (_, _) =>
            {
                Result = index;
                DialogResult = true;
            };
            row.Children.Add(b);
        }

        var panel = new StackPanel { Margin = new Thickness(26, 24, 26, 20), MinWidth = 340 };
        panel.Children.Add(head);
        panel.Children.Add(row);
        Content = panel;
        Result = cancelIndex;
    }

    public int Result { get; private set; }

    /// <summary>Muestra el cuadro y devuelve el número del botón pulsado (o el de cancelar si se cierra con Esc).</summary>
    public static int Show(Window? owner, string message, Kind kind, string[] buttons, int defaultIndex = 0, int cancelIndex = -1)
    {
        var d = new MessageDialog(message, kind, buttons, defaultIndex, cancelIndex);
        if (owner is { IsVisible: true }) d.Owner = owner;
        else d.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        d.ShowDialog();
        return d.Result;
    }
}
