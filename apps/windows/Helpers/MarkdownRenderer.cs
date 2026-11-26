using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Windows.System;

namespace NopeNote.Windows.Helpers;

/// <summary>
/// Small Markdown renderer for the preview pane: headings, emphasis, lists,
/// checkboxes, quotes, code fences, tables and local images.
/// </summary>
internal static class MarkdownRenderer
{
    private static readonly Regex ImagePattern = new(
        @"^!\[(?<alt>[^\]]*)\]\((?<path>[^)]+)\)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex LinkLinePattern = new(
        @"^\[(?<text>[^\]]+)\]\((?<path>[^)]+)\)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex InlinePattern = new(
        @"(?<bold>\*\*(?<bt>[^*]+)\*\*)|(?<italic>\*(?<it>[^*]+)\*)|(?<code>`(?<ct>[^`]+)`)|(?<link>\[(?<lt>[^\]]+)\]\((?<lp>[^)]+)\))|(?<wiki>\[\[(?<wt>[^\]]+)\]\])",
        RegexOptions.Compiled);

    private static readonly Regex TableSeparator = new(
        @"^\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)+\|?$",
        RegexOptions.Compiled);

    internal static UIElement Render(string markdown, string vaultPath)
    {
        var panel = new StackPanel { Spacing = 6 };
        string[] lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        int index = 0;
        while (index < lines.Length)
        {
            string line = lines[index];

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                index = AddCodeBlock(panel, lines, index);
                continue;
            }

            if (IsTableStart(lines, index))
            {
                index = AddTable(panel, lines, index);
                continue;
            }

            if (line.Length == 0)
            {
                index++;
                continue;
            }

            Match image = ImagePattern.Match(line.Trim());
            if (image.Success)
            {
                AddImage(panel, vaultPath, image.Groups["path"].Value, image.Groups["alt"].Value);
                index++;
                continue;
            }

            Match linkLine = LinkLinePattern.Match(line.Trim());
            if (linkLine.Success && !linkLine.Groups["path"].Value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                AddFileLink(panel, vaultPath, linkLine.Groups["text"].Value, linkLine.Groups["path"].Value);
                index++;
                continue;
            }

            string trimmed = line.TrimStart();
            if (trimmed.StartsWith('#'))
            {
                int level = 0;
                while (level < trimmed.Length && trimmed[level] == '#')
                {
                    level++;
                }

                string heading = trimmed[level..].Trim();
                if (heading.Length > 0 && level <= 6)
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = heading,
                        FontSize = level switch { 1 => 24, 2 => 20, 3 => 17, _ => 15 },
                        FontWeight = FontWeights.SemiBold,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, level <= 2 ? 10 : 4, 0, 2),
                    });
                }

                index++;
                continue;
            }

            if (trimmed.StartsWith("- [ ]", StringComparison.Ordinal) ||
                trimmed.StartsWith("- [x]", StringComparison.OrdinalIgnoreCase))
            {
                bool done = trimmed.StartsWith("- [x]", StringComparison.OrdinalIgnoreCase);
                AddCheckItem(panel, done, trimmed[5..].Trim());
                index++;
                continue;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal) ||
                trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                AddParagraph(panel, "•  " + trimmed[2..], new Thickness(14, 0, 0, 0));
                index++;
                continue;
            }

            int dot = trimmed.IndexOf(". ", StringComparison.Ordinal);
            if (dot > 0 && dot <= 3 && int.TryParse(trimmed[..dot], out _))
            {
                AddParagraph(panel, trimmed, new Thickness(14, 0, 0, 0));
                index++;
                continue;
            }

            if (trimmed.StartsWith("> ", StringComparison.Ordinal))
            {
                TextBlock quote = AddParagraph(panel, trimmed[2..], new Thickness(12, 0, 0, 0));
                quote.FontStyle = global::Windows.UI.Text.FontStyle.Italic;
                quote.Foreground = ResourceBrush("TextFillColorSecondaryBrush");
                index++;
                continue;
            }

            AddParagraph(panel, line, new Thickness(0));
            index++;
        }

        if (panel.Children.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = Strings.Get("NotesPreviewEmpty"),
                Foreground = ResourceBrush("TextFillColorSecondaryBrush"),
            });
        }

        return panel;
    }

    private static int AddCodeBlock(StackPanel panel, string[] lines, int start)
    {
        int end = start + 1;
        while (end < lines.Length && !lines[end].TrimStart().StartsWith("```", StringComparison.Ordinal))
        {
            end++;
        }

        string code = string.Join("\n", lines[(start + 1)..end]);
        TextBlock text = new()
        {
            Text = code.Length == 0 ? " " : code,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
        };

        panel.Children.Add(new Border
        {
            Background = ResourceBrush("CardBackgroundFillColorSecondaryBrush"),
            BorderBrush = ResourceBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 10, 12, 10),
            Child = text,
        });

        return Math.Min(end + 1, lines.Length);
    }

    private static bool IsTableStart(string[] lines, int index)
    {
        if (index + 1 >= lines.Length || lines[index].IndexOf('|') < 0)
        {
            return false;
        }

        return TableSeparator.IsMatch(lines[index + 1].Trim());
    }

    private static int AddTable(StackPanel panel, string[] lines, int start)
    {
        string[] headers = SplitRow(lines[start]);
        int end = start + 2;
        var rows = new List<string[]>();
        while (end < lines.Length && lines[end].IndexOf('|') >= 0)
        {
            rows.Add(SplitRow(lines[end]));
            end++;
        }

        Grid grid = new() { ColumnSpacing = 0, RowSpacing = 0 };
        for (int column = 0; column < headers.Length; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (int row = 0; row <= rows.Count; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (int column = 0; column < headers.Length; column++)
        {
            AddCell(grid, column, 0, headers[column], isHeader: true);
        }

        for (int row = 0; row < rows.Count; row++)
        {
            for (int column = 0; column < headers.Length; column++)
            {
                string value = column < rows[row].Length ? rows[row][column] : string.Empty;
                AddCell(grid, column, row + 1, value, isHeader: false);
            }
        }

        panel.Children.Add(new Border
        {
            BorderBrush = ResourceBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = grid,
        });

        return end;
    }

    private static void AddCell(Grid grid, int column, int row, string value, bool isHeader)
    {
        TextBlock text = BuildInline(value);
        text.Padding = new Thickness(8, 5, 8, 5);
        text.FontWeight = isHeader ? FontWeights.SemiBold : FontWeights.Normal;

        Border cell = new()
        {
            BorderBrush = ResourceBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(0, 0, column == 0 ? 0 : 1, row == 0 ? 0 : 1),
            Background = isHeader ? ResourceBrush("CardBackgroundFillColorSecondaryBrush") : null,
            Child = text,
        };

        Grid.SetColumn(cell, column);
        Grid.SetRow(cell, row);
        grid.Children.Add(cell);
    }

    private static string[] SplitRow(string line)
    {
        string trimmed = line.Trim().Trim('|');
        return Array.ConvertAll(trimmed.Split('|'), cell => cell.Trim());
    }

    private static void AddCheckItem(StackPanel panel, bool done, string text)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        stack.Children.Add(new TextBlock
        {
            Text = done ? "☑" : "☐",
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ResourceBrush("TextFillColorSecondaryBrush"),
        });

        TextBlock label = BuildInline(text);
        label.Opacity = done ? 0.6 : 1.0;
        stack.Children.Add(label);
        panel.Children.Add(stack);
    }

    private static void AddImage(StackPanel panel, string vaultPath, string relativePath, string alt)
    {
        string? localPath = ResolveVaultPath(vaultPath, relativePath);

        if (localPath is null || !File.Exists(localPath))
        {
            panel.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(alt) ? relativePath : alt,
                Foreground = ResourceBrush("TextFillColorSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        var bitmap = new BitmapImage(new Uri("file:///" + localPath.Replace('\\', '/')));
        panel.Children.Add(new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxHeight = 360,
        });
    }

    private static void AddFileLink(StackPanel panel, string vaultPath, string text, string relativePath)
    {
        string? localPath = ResolveVaultPath(vaultPath, relativePath);

        HyperlinkButton link = new()
        {
            Content = text,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        link.Click += async (_, _) =>
        {
            if (localPath is not null && File.Exists(localPath))
            {
                await Launcher.LaunchFileAsync(
                    await global::Windows.Storage.StorageFile.GetFileFromPathAsync(localPath));
            }
        };

        panel.Children.Add(link);
    }

    private static string? ResolveVaultPath(string vaultPath, string relativePath)
    {
        // The spec writes attachment links relative to the note file,
        // for example ../atts/<sha256>.png; map them onto .vault/atts.
        string relative = relativePath.Trim().Replace('\\', '/');
        if (relative.StartsWith("../atts/", StringComparison.OrdinalIgnoreCase))
        {
            relative = ".vault/atts/" + relative["../atts/".Length..];
        }

        string root = Path.GetFullPath(vaultPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        string localPath = Path.GetFullPath(
            Path.Combine(vaultPath, relative.Replace('/', Path.DirectorySeparatorChar)));
        return localPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? localPath
            : null;
    }

    private static TextBlock AddParagraph(StackPanel panel, string text, Thickness margin)
    {
        TextBlock block = BuildInline(text);
        block.Margin = margin;
        panel.Children.Add(block);
        return block;
    }

    private static TextBlock BuildInline(string text)
    {
        TextBlock block = new()
        {
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            LineHeight = 20,
        };

        int position = 0;
        foreach (Match match in InlinePattern.Matches(text))
        {
            if (match.Index > position)
            {
                block.Inlines.Add(new Run { Text = text[position..match.Index] });
            }

            if (match.Groups["bold"].Success)
            {
                block.Inlines.Add(new Run
                {
                    Text = match.Groups["bt"].Value,
                    FontWeight = FontWeights.SemiBold,
                });
            }
            else if (match.Groups["italic"].Success)
            {
                block.Inlines.Add(new Run
                {
                    Text = match.Groups["it"].Value,
                    FontStyle = global::Windows.UI.Text.FontStyle.Italic,
                });
            }
            else if (match.Groups["code"].Success)
            {
                block.Inlines.Add(new Run
                {
                    Text = match.Groups["ct"].Value,
                    FontFamily = new FontFamily("Consolas"),
                    Foreground = ResourceBrush("AccentTextFillColorPrimaryBrush"),
                });
            }
            else if (match.Groups["link"].Success)
            {
                string label = match.Groups["lt"].Value;
                string target = match.Groups["lp"].Value;
                Hyperlink hyperlink = new();
                hyperlink.Inlines.Add(new Run { Text = label });
                if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    hyperlink.NavigateUri = new Uri(target);
                }

                block.Inlines.Add(hyperlink);
            }
            else if (match.Groups["wiki"].Success)
            {
                block.Inlines.Add(new Run
                {
                    Text = match.Groups["wt"].Value,
                    Foreground = ResourceBrush("AccentTextFillColorPrimaryBrush"),
                });
            }

            position = match.Index + match.Length;
        }

        if (position < text.Length)
        {
            block.Inlines.Add(new Run { Text = text[position..] });
        }

        return block;
    }

    private static Brush ResourceBrush(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out object? value) && value is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }
}
