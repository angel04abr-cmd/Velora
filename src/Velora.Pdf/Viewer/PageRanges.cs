// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.RegularExpressions;

namespace Velora.Pdf.Viewer;

/// <summary>Páginas escritas a mano, como «1-3, 7, 10-12».</summary>
public static partial class PageRanges
{
    [GeneratedRegex(@"^(\d+)\s*(?:[-–]\s*(\d+))?$")]
    private static partial Regex PartRegex();

    /// <summary>Páginas elegidas (0 = primera), sin repetir y en el orden escrito. <c>null</c> si el texto no es válido (con el motivo en <paramref name="problem"/>).</summary>
    public static List<int>? Parse(string text, int pageCount, out string? problem)
    {
        problem = null;
        var pages = new List<int>();
        var seen = new HashSet<int>();
        foreach (string part in text.Split(',', ';').Select(p => p.Trim()).Where(p => p.Length > 0))
        {
            var m = PartRegex().Match(part);
            if (!m.Success)
            {
                problem = $"«{part}» no es un número ni un intervalo (por ejemplo, 3 o 5-8).";
                return null;
            }

            if (!int.TryParse(m.Groups[1].Value, out int a) || !int.TryParse(m.Groups[2].Success ? m.Groups[2].Value : m.Groups[1].Value, out int b))
            {
                problem = $"«{part}» no es un número de página válido.";
                return null;
            }

            if (a > b) (a, b) = (b, a);
            if (a < 1 || b > pageCount)
            {
                problem = $"El documento tiene {pageCount} páginas.";
                return null;
            }

            for (int p = a; p <= b; p++)
                if (seen.Add(p - 1)) pages.Add(p - 1);
        }

        if (pages.Count == 0)
        {
            problem = "Escribe las páginas que quieres imprimir, por ejemplo 1-3, 7.";
            return null;
        }

        return pages;
    }
}
