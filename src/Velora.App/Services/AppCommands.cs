// SPDX-License-Identifier: GPL-3.0-or-later
using System.Windows.Input;

namespace Velora.App;

/// <summary>Órdenes de la aplicación con sus atajos de teclado (los atajos siguen los de Papers cuando existen).</summary>
internal static class AppCommands
{
    private static RoutedUICommand Make(string text, string name, params (Key Key, ModifierKeys Mod)[] gestures)
    {
        var cmd = new RoutedUICommand(text, name, typeof(AppCommands));
        foreach (var (k, m) in gestures) cmd.InputGestures.Add(new KeyGesture(k, m));
        return cmd;
    }

    private const ModifierKeys Ctrl = ModifierKeys.Control, Shift = ModifierKeys.Shift, Alt = ModifierKeys.Alt, None = ModifierKeys.None;

    public static readonly RoutedUICommand Open = Make("Abrir…", nameof(Open), (Key.O, Ctrl));
    public static readonly RoutedUICommand NewWindow = Make("Nueva ventana", nameof(NewWindow), (Key.N, Ctrl));
    public static readonly RoutedUICommand CloseDocument = Make("Cerrar el documento", nameof(CloseDocument), (Key.W, Ctrl));
    public static readonly RoutedUICommand PreviousPage = Make("Página anterior", nameof(PreviousPage), (Key.PageUp, Ctrl));
    public static readonly RoutedUICommand NextPage = Make("Página siguiente", nameof(NextPage), (Key.PageDown, Ctrl));
    public static readonly RoutedUICommand Save = Make("Guardar", nameof(Save), (Key.S, Ctrl));
    public static readonly RoutedUICommand SaveCopy = Make("Guardar una copia…", nameof(SaveCopy), (Key.S, Ctrl | Shift));
    public static readonly RoutedUICommand Print = Make("Imprimir…", nameof(Print), (Key.P, Ctrl));
    public static readonly RoutedUICommand Find = Make("Buscar", nameof(Find), (Key.F, Ctrl));
    public static readonly RoutedUICommand FindNext = Make("Resultado siguiente", nameof(FindNext), (Key.F3, None), (Key.G, Ctrl));
    public static readonly RoutedUICommand FindPrevious = Make("Resultado anterior", nameof(FindPrevious), (Key.F3, Shift), (Key.G, Ctrl | Shift));
    public static readonly RoutedUICommand GoToPage = Make("Ir a la página…", nameof(GoToPage), (Key.L, Ctrl));
    public static readonly RoutedUICommand ZoomIn = Make("Acercar", nameof(ZoomIn), (Key.OemPlus, Ctrl), (Key.Add, Ctrl));
    public static readonly RoutedUICommand ZoomOut = Make("Alejar", nameof(ZoomOut), (Key.OemMinus, Ctrl), (Key.Subtract, Ctrl));
    public static readonly RoutedUICommand FitWidth = Make("Ajustar al ancho", nameof(FitWidth), (Key.D0, Ctrl), (Key.NumPad0, Ctrl));
    public static readonly RoutedUICommand FitPage = Make("Ajustar a la página", nameof(FitPage), (Key.D9, Ctrl), (Key.NumPad9, Ctrl));
    public static readonly RoutedUICommand Zoom100 = Make("Tamaño real (100 %)", nameof(Zoom100), (Key.D1, Ctrl), (Key.NumPad1, Ctrl));
    public static readonly RoutedUICommand RotateRight = Make("Girar a la derecha", nameof(RotateRight), (Key.Right, Ctrl));
    public static readonly RoutedUICommand RotateLeft = Make("Girar a la izquierda", nameof(RotateLeft), (Key.Left, Ctrl));
    public static readonly RoutedUICommand Presentation = Make("Presentación", nameof(Presentation), (Key.F5, None));
    public static readonly RoutedUICommand FullScreen = Make("Pantalla completa", nameof(FullScreen), (Key.F11, None));
    public static readonly RoutedUICommand Sidebar = Make("Mostrar u ocultar la barra lateral", nameof(Sidebar), (Key.F9, None));
    public static readonly RoutedUICommand Properties = Make("Propiedades", nameof(Properties), (Key.Enter, Alt));
    public static readonly RoutedUICommand Back = Make("Volver atrás", nameof(Back), (Key.Left, Alt));
    public static readonly RoutedUICommand Forward = Make("Ir hacia delante", nameof(Forward), (Key.Right, Alt));
    public static readonly RoutedUICommand TwoPages = Make("Dos páginas", nameof(TwoPages));
    public static readonly RoutedUICommand CoverAlone = Make("Portada suelta", nameof(CoverAlone));
    public static readonly RoutedUICommand About = Make("Acerca de Velora", nameof(About));
    public static readonly RoutedUICommand Escape = Make("Cerrar", nameof(Escape), (Key.Escape, None));
    public static readonly RoutedUICommand Highlight = Make("Resaltar", nameof(Highlight));
    public static readonly RoutedUICommand Underline = Make("Subrayar", nameof(Underline));
    public static readonly RoutedUICommand Squiggly = Make("Subrayado ondulado", nameof(Squiggly));
    public static readonly RoutedUICommand Strike = Make("Tachar", nameof(Strike));
    public static readonly RoutedUICommand NoteTool = Make("Añadir una nota", nameof(NoteTool));
    public static readonly RoutedUICommand InkTool = Make("Dibujar a mano alzada", nameof(InkTool));
    public static readonly RoutedUICommand NoteToolMenu = Make("Añadir una nota", nameof(NoteToolMenu));
    public static readonly RoutedUICommand InkToolMenu = Make("Dibujar a mano alzada", nameof(InkToolMenu));
    public static readonly RoutedUICommand ColorMenu = Make("Color de las anotaciones…", nameof(ColorMenu));
    public static readonly RoutedUICommand Exit = Make("Salir", nameof(Exit));
    public static readonly RoutedUICommand Manual = Make("Manual de uso", nameof(Manual), (Key.F1, None));
    public static readonly RoutedUICommand CheckUpdates = Make("Buscar actualizaciones…", nameof(CheckUpdates));
    public static readonly RoutedUICommand AutoUpdates = Make("Buscar actualizaciones automáticamente", nameof(AutoUpdates));
    public static readonly RoutedUICommand OpenFolder = Make("Mostrar el archivo en el Explorador", nameof(OpenFolder));
}
