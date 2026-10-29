// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Velora.App;

/// <summary>
/// Dibuja un Markdown sencillo (títulos, párrafos, listas, tablas, citas, código, negrita, cursiva, código en línea y enlaces) como texto con formato,
/// para el manual, las licencias y las novedades de cada versión. Solo cubre lo que usan esos textos.
/// </summary>
internal static partial class RichText
{
    [GeneratedRegex(@"\*\*(.+?)\*\*|__(.+?)__|`([^`]+)`|\[([^\]]+)\]\(([^)\s]+)\)|<(https?://[^>\s]+)>|(?<![\*\w])\*(?!\s)(.+?)(?<!\s)\*(?![\*\w])|(?<![_\w])_(?!\s)(.+?)(?<!\s)_(?![_\w])")]
    private static partial Regex InlineRegex();

    [GeneratedRegex(@"^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$")]
    private static partial Regex TableSeparator();

    public static FlowDocument Build(string markdown, double fontSize = 14, bool compact = false)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = fontSize,
            PagePadding = new Thickness(compact ? 0 : 4),
            LineHeight = fontSize * 1.45,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextAlignment = TextAlignment.Left,
        };
        doc.SetResourceReference(FlowDocument.ForegroundProperty, "TextFillColorPrimaryBrush");

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var para = new List<string>();
        List? list = null;
        bool listOrdered = false;

        void FlushPara()
        {
            if (para.Count == 0) return;
            var p = new Paragraph { Margin = new Thickness(0, 0, 0, fontSize * 0.7) };
            AddInlines(p.Inlines, string.Join(" ", para));
            doc.Blocks.Add(p);
            para.Clear();
        }

        void FlushList()
        {
            if (list == null) return;
            doc.Blocks.Add(list);
            list = null;
        }

        for (int i = 0; i < lines.Length; i++)
        {
            string raw = lines[i];
            string line = raw.TrimEnd();
            string trimmed = line.TrimStart();

            if (trimmed.StartsWith("```"))
            {
                FlushPara();
                FlushList();
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].TrimStart().StartsWith("```"); i++) code.Add(lines[i]);
                var block = new Paragraph(new Run(string.Join("\n", code)))
                {
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    FontSize = fontSize * 0.92,
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 0, 0, fontSize * 0.7),
                };
                block.SetResourceReference(TextElement.BackgroundProperty, "ControlFillColorSecondaryBrush");
                doc.Blocks.Add(block);
                continue;
            }

            if (trimmed.Length == 0)
            {
                FlushPara();
                FlushList();
                continue;
            }

            var heading = Regex.Match(trimmed, @"^(#{1,4})\s+(.*)$");
            if (heading.Success)
            {
                FlushPara();
                FlushList();
                int level = heading.Groups[1].Length;
                double size = level switch { 1 => fontSize * 1.7, 2 => fontSize * 1.35, 3 => fontSize * 1.15, _ => fontSize };
                var h = new Paragraph { FontSize = size, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, level == 1 ? 0 : fontSize * 0.9, 0, fontSize * 0.45), LineHeight = size * 1.3 };
                AddInlines(h.Inlines, heading.Groups[2].Value.Trim());
                if (level <= 2) h.SetResourceReference(TextElement.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
                doc.Blocks.Add(h);
                continue;
            }

            if (Regex.IsMatch(trimmed, @"^(-{3,}|\*{3,}|_{3,})$"))
            {
                FlushPara();
                FlushList();
                var rule = new Paragraph { Margin = new Thickness(0, 4, 0, 10), FontSize = 2, BorderThickness = new Thickness(0, 0, 0, 1) };
                rule.SetResourceReference(Block.BorderBrushProperty, "DividerStrokeColorDefaultBrush");
                doc.Blocks.Add(rule);
                continue;
            }

            // Tabla: línea con barras seguida de la separadora (---|---)
            if (trimmed.Contains('|') && i + 1 < lines.Length && TableSeparator().IsMatch(lines[i + 1]) && lines[i + 1].Contains('-'))
            {
                FlushPara();
                FlushList();
                var rows = new List<string[]> { SplitRow(trimmed) };
                int j = i + 2;
                for (; j < lines.Length && lines[j].Contains('|') && lines[j].Trim().Length > 0; j++) rows.Add(SplitRow(lines[j].Trim()));
                doc.Blocks.Add(BuildTable(rows, fontSize));
                i = j - 1;
                continue;
            }

            if (trimmed.StartsWith('>'))
            {
                FlushPara();
                FlushList();
                var quote = new Paragraph { Margin = new Thickness(0, 0, 0, fontSize * 0.7), Padding = new Thickness(12, 4, 4, 4), BorderThickness = new Thickness(3, 0, 0, 0), FontStyle = FontStyles.Italic };
                quote.SetResourceReference(Block.BorderBrushProperty, "AccentFillColorDefaultBrush");
                AddInlines(quote.Inlines, trimmed.TrimStart('>').Trim());
                doc.Blocks.Add(quote);
                continue;
            }

            var bullet = Regex.Match(line, @"^(\s*)([-*+]|\d+[.)])\s+(.*)$");
            if (bullet.Success)
            {
                FlushPara();
                bool ordered = char.IsDigit(bullet.Groups[2].Value[0]);
                if (list == null || ordered != listOrdered)
                {
                    FlushList();
                    listOrdered = ordered;
                    list = new List { MarkerStyle = ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc, Margin = new Thickness(fontSize * 1.2, 0, 0, fontSize * 0.5), Padding = new Thickness(0) };
                }

                var item = new Paragraph { Margin = new Thickness(0, 0, 0, fontSize * 0.25) };
                AddInlines(item.Inlines, bullet.Groups[3].Value);
                list.ListItems.Add(new ListItem(item));
                continue;
            }

            // Continuación de un elemento de lista (línea con sangría)
            if (list != null && raw.StartsWith("  ") && list.ListItems.LastListItem?.Blocks.FirstBlock is Paragraph last)
            {
                last.Inlines.Add(new Run(" "));
                AddInlines(last.Inlines, trimmed);
                continue;
            }

            FlushList();
            para.Add(trimmed);
        }

        FlushPara();
        FlushList();
        return doc;
    }

    private static string[] SplitRow(string row) =>
        row.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();

    private static Table BuildTable(List<string[]> rows, double fontSize)
    {
        int cols = rows.Max(r => r.Length);
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, fontSize * 0.8) };
        for (int c = 0; c < cols; c++) table.Columns.Add(new TableColumn());
        var group = new TableRowGroup();
        for (int r = 0; r < rows.Count; r++)
        {
            var tr = new TableRow();
            for (int c = 0; c < cols; c++)
            {
                var cell = new TableCell { Padding = new Thickness(8, 4, 8, 4), BorderThickness = new Thickness(0, 0, 0, 1) };
                cell.SetResourceReference(Block.BorderBrushProperty, "DividerStrokeColorDefaultBrush");
                var p = new Paragraph { Margin = new Thickness(0), FontWeight = r == 0 ? FontWeights.SemiBold : FontWeights.Normal };
                AddInlines(p.Inlines, c < rows[r].Length ? rows[r][c] : "");
                cell.Blocks.Add(p);
                tr.Cells.Add(cell);
            }

            group.Rows.Add(tr);
        }

        table.RowGroups.Add(group);
        return table;
    }

    /// <summary>Texto con **negrita**, *cursiva*, `código` y [enlaces](url).</summary>
    public static void AddInlines(InlineCollection target, string text)
    {
        int pos = 0;
        foreach (Match m in InlineRegex().Matches(text))
        {
            if (m.Index > pos) target.Add(new Run(Unescape(text[pos..m.Index])));
            pos = m.Index + m.Length;
            if (m.Groups[1].Success) { var b = new Bold(); AddInlines(b.Inlines, m.Groups[1].Value); target.Add(b); }
            else if (m.Groups[2].Success) { var b = new Bold(); AddInlines(b.Inlines, m.Groups[2].Value); target.Add(b); }
            else if (m.Groups[3].Success)
            {
                var code = new Run(m.Groups[3].Value) { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 0.92 * 14 };
                code.SetResourceReference(TextElement.BackgroundProperty, "ControlFillColorSecondaryBrush");
                target.Add(code);
            }
            else if (m.Groups[4].Success) target.Add(MakeLink(m.Groups[4].Value, m.Groups[5].Value));
            else if (m.Groups[6].Success) target.Add(MakeLink(m.Groups[6].Value, m.Groups[6].Value));
            else
            {
                var i = new Italic();
                AddInlines(i.Inlines, m.Groups[7].Success ? m.Groups[7].Value : m.Groups[8].Value);
                target.Add(i);
            }
        }

        if (pos < text.Length) target.Add(new Run(Unescape(text[pos..])));
    }

    private static string Unescape(string s) => Regex.Replace(s, @"\\([\\`*_{}\[\]()#+\-.!|>])", "$1");

    private static Inline MakeLink(string text, string url)
    {
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return new Run(text); // enlaces a archivos del repositorio: sin destino útil dentro del programa
        var link = new Hyperlink(new Run(text)) { NavigateUri = new Uri(url, UriKind.Absolute), ToolTip = url };
        link.RequestNavigate += (_, e) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch
            {
                // sin navegador: no se puede hacer más
            }

            e.Handled = true;
        };
        return link;
    }
}
