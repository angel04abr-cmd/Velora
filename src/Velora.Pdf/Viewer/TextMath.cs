// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;

namespace Velora.Pdf.Viewer;

/// <summary>Cálculos sobre el texto de una página: qué carácter hay bajo el ratón, rectángulos de selección y texto copiado.</summary>
public static class TextMath
{
    /// <summary>
    /// Carácter más cercano al punto (coordenadas de página). Devuelve -1 si la página no tiene texto.
    /// <paramref name="inside"/> indica si el punto cae realmente sobre la caja de un carácter.
    /// </summary>
    public static int HitTest(PageContent c, float x, float y, out bool inside)
    {
        inside = false;
        int best = -1;
        float bestDist = float.MaxValue;
        var b = c.Boxes;
        for (int i = 0; i < c.Chars.Length; i++)
        {
            int k = 4 * i;
            if (b[k + 2] <= b[k] || b[k + 3] <= b[k + 1]) continue;
            float dx = x < b[k] ? b[k] - x : x > b[k + 2] ? x - b[k + 2] : 0;
            float dy = y < b[k + 1] ? b[k + 1] - y : y > b[k + 3] ? y - b[k + 3] : 0;
            // Se da más peso a la distancia vertical: se prefiere el carácter de la misma línea.
            float d = dx * dx + dy * dy * 4;
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
                if (d == 0) { inside = true; break; }
            }
        }

        return best;
    }

    /// <summary>Hay texto bajo el punto (con algo de margen), para decidir el cursor.</summary>
    public static bool IsOverText(PageContent c, float x, float y, float tolerance = 1.5f)
    {
        var b = c.Boxes;
        for (int i = 0; i < c.Chars.Length; i++)
        {
            int k = 4 * i;
            if (b[k + 2] <= b[k] || b[k + 3] <= b[k + 1]) continue;
            if (x >= b[k] - tolerance && x <= b[k + 2] + tolerance && y >= b[k + 1] - tolerance && y <= b[k + 3] + tolerance)
                return !char.IsWhiteSpace(CharOf(c.Chars[i]));
        }

        return false;
    }

    private static char CharOf(int cp) => cp is > 0 and < 0x10000 ? (char)cp : 'x';

    /// <summary>Rectángulos (uno por línea) que cubren los caracteres from..to, ambos incluidos.</summary>
    public static List<RectF> RectsForRange(PageContent c, int from, int to)
    {
        var result = new List<RectF>();
        if (c.CharCount == 0) return result;
        from = Math.Max(0, from);
        to = Math.Min(c.CharCount - 1, to);
        RectF? cur = null;
        for (int i = from; i <= to; i++)
        {
            if (!c.HasBox(i)) continue;
            var b = c.BoxOf(i);
            if (cur == null)
            {
                cur = b;
                continue;
            }

            var r = cur.Value;
            float overlap = Math.Min(r.Bottom, b.Bottom) - Math.Max(r.Top, b.Top);
            float minH = Math.Min(r.Height, b.Height);
            bool sameLine = overlap > minH * 0.5f && b.Left >= r.Left - 2;
            if (sameLine)
            {
                cur = new RectF(r.Left, Math.Min(r.Top, b.Top), Math.Max(r.Right, b.Right), Math.Max(r.Bottom, b.Bottom));
            }
            else
            {
                result.Add(r);
                cur = b;
            }
        }

        if (cur != null) result.Add(cur.Value);
        return result;
    }

    /// <summary>Texto de los caracteres from..to (ambos incluidos), con saltos de línea normales de Windows.</summary>
    public static string TextOfRange(PageContent c, int from, int to)
    {
        var sb = new StringBuilder();
        from = Math.Max(0, from);
        to = Math.Min(c.CharCount - 1, to);
        for (int i = from; i <= to; i++) Append(sb, c.Chars[i]);
        return sb.ToString();
    }

    public static void Append(StringBuilder sb, int cp)
    {
        if (cp == 0 || cp == 0xFFFE || cp == 0xFFFF) return;
        if (cp == '\r')
        {
            sb.Append("\r\n");
            return;
        }

        if (cp == '\n')
        {
            if (sb.Length == 0 || sb[^1] != '\n') sb.Append("\r\n");
            return;
        }

        if (cp is >= 0xD800 and <= 0xDFFF or > 0x10FFFF) return;
        if (cp < 0x10000) sb.Append((char)cp);
        else sb.Append(char.ConvertFromUtf32(cp));
    }

    /// <summary>Palabra que contiene el carácter dado.</summary>
    public static (int From, int To) WordAt(PageContent c, int index)
    {
        if (c.CharCount == 0) return (0, -1);
        index = Math.Clamp(index, 0, c.CharCount - 1);
        bool Word(int i) => c.Chars[i] > 32 && !(c.Chars[i] < 0x10000 && char.IsWhiteSpace((char)c.Chars[i]));
        if (!Word(index)) return (index, index);
        int a = index, b = index;
        while (a > 0 && Word(a - 1)) a--;
        while (b < c.CharCount - 1 && Word(b + 1)) b++;
        return (a, b);
    }

    /// <summary>Caracteres de la misma línea que el dado (sin los espacios de los extremos).</summary>
    public static (int From, int To) LineAt(PageContent c, int index)
    {
        if (c.CharCount == 0) return (0, -1);
        index = Math.Clamp(index, 0, c.CharCount - 1);
        if (!c.HasBox(index)) return (index, index);
        var r = c.BoxOf(index);
        bool Blank(int i) => c.Chars[i] <= 32 || (c.Chars[i] < 0x10000 && char.IsWhiteSpace((char)c.Chars[i]));
        bool OnLine(int i)
        {
            var b = c.BoxOf(i);
            float overlap = Math.Min(r.Bottom, b.Bottom) - Math.Max(r.Top, b.Top);
            return overlap > Math.Min(r.Height, b.Height) * 0.4f;
        }

        int from = index, to = index;
        for (int i = index + 1; i < c.CharCount; i++)
        {
            if (c.Chars[i] is 10 or 13) break;
            if (Blank(i) || !c.HasBox(i)) continue;
            if (!OnLine(i)) break;
            to = i;
        }

        for (int i = index - 1; i >= 0; i--)
        {
            if (c.Chars[i] is 10 or 13) break;
            if (Blank(i) || !c.HasBox(i)) continue;
            if (!OnLine(i)) break;
            from = i;
        }

        return (from, to);
    }
}
