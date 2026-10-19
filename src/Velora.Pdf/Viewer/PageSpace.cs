// SPDX-License-Identifier: GPL-3.0-or-later
namespace Velora.Pdf.Viewer;

/// <summary>
/// Pasa de las coordenadas de página (puntos, origen arriba-izquierda, página sin girar) a las que se ven
/// en pantalla cuando el usuario gira la vista 0, 90, 180 o 270 grados en el sentido de las agujas del reloj.
/// </summary>
public static class PageSpace
{
    public static (double W, double H) DisplaySize(int rot, double w, double h) => (rot & 1) != 0 ? (h, w) : (w, h);

    public static (double X, double Y) ToDisplay(int rot, double w, double h, double u, double v) => (rot & 3) switch
    {
        1 => (h - v, u),
        2 => (w - u, h - v),
        3 => (v, w - u),
        _ => (u, v),
    };

    public static (double U, double V) FromDisplay(int rot, double w, double h, double x, double y) => (rot & 3) switch
    {
        1 => (y, h - x),
        2 => (w - x, h - y),
        3 => (w - y, x),
        _ => (x, y),
    };

    public static RectF ToDisplay(int rot, double w, double h, RectF r)
    {
        var (x1, y1) = ToDisplay(rot, w, h, r.Left, r.Top);
        var (x2, y2) = ToDisplay(rot, w, h, r.Right, r.Bottom);
        return new RectF((float)Math.Min(x1, x2), (float)Math.Min(y1, y2), (float)Math.Max(x1, x2), (float)Math.Max(y1, y2));
    }
}
