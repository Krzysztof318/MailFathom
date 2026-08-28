// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Client.Backend.Mail;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Documents;

namespace MailFathom.Client.Presentation.Spaces.Mail.Reading;

/// <summary>Draws a reduced message body with the pane's own controls.</summary>
/// <remarks>
/// <para>
/// C# markup rather than XAML because the markup is computed: the shape is a tree a stranger wrote, known only at run
/// time, and a template selector over eight block types would state the same recursion in a less readable place. This
/// is the case <c>frontend/src/AGENTS.md</c> § <em>XAML first</em> admits, and the styles every element below reaches
/// are authored in <c>Styles/MailBodyView.xaml</c> rather than written here, so nothing in this file names a colour, a
/// font, or a size.
/// </para>
/// <para>
/// Nothing here parses anything. Every value it reads is text, a number, a colour, an identity, or a member of a closed
/// set, and each becomes a property of a typed control — so a message cannot introduce an element, a handler, or a
/// style that reaches past the pane. What a message asks for that this cannot express is dropped rather than
/// approximated.
/// </para>
/// <para>
/// A link is never followed from here. Following one is a question put to the reader, so each carries the target it
/// actually goes to up to the pane, which asks before anything leaves the application.
/// </para>
/// </remarks>
internal sealed class MailBodyDrawing
{
    /// <summary>How deep a message may nest before the drawing stops descending.</summary>
    /// <remarks>
    /// The deployment already bounds the tree it builds, so this cannot be reached by an ordinary message. It is here
    /// because the head is drawing a document from a service it does not control, and a bound that protects the thread
    /// that draws belongs on the side that would freeze.
    /// </remarks>
    internal const int MaximumDepth = 32;

    private const string ParagraphStyleKey = "MailBodyParagraphStyle";
    private const string PreformattedStyleKey = "MailBodyPreformattedStyle";
    private const string UnsupportedStyleKey = "MailBodyUnsupportedStyle";
    private const string QuoteStyleKey = "MailBodyQuoteStyle";
    private const string SeparatorStyleKey = "MailBodySeparatorStyle";
    private const string CellStyleKey = "MailBodyTableCellStyle";
    private const string HeaderCellStyleKey = "MailBodyTableHeaderCellStyle";
    private const string MonospaceFontKey = "MailBodyMonospaceFontFamily";
    private const string BlockSpacingKey = "MailBodyBlockSpacing";
    /// <summary>Material's own key for what this pane draws on, read as each message is drawn.</summary>
    /// <remarks>
    /// The design system's key rather than one of this application's, because the lookup has to happen per drawing: a
    /// key of ours aliasing this one with <c>StaticResource</c> would resolve once when the dictionary loaded and
    /// freeze the surface at whichever theme was in force then, which is exactly the wrong background to judge a
    /// sender's colour against for every message drawn after a reader changes theme.
    /// </remarks>
    private const string SurfaceBrushKey = "SurfaceBrush";

    /// <summary>Material's own key for what this pane draws with, read per drawing for the reason above.</summary>
    private const string InkBrushKey = "OnSurfaceBrush";

    /// <summary>The contrast a sender's colour clears to be drawn, which is what WCAG asks of body text.</summary>
    private const double MinimumContrast = 4.5;

    private readonly MailBodyWords words;
    private readonly Action<MailBodyLink, string> follow;
    private readonly List<PendingPicture> pictures = [];

    /// <summary>What the words being drawn actually sit on, where a cell painted something other than the surface.</summary>
    /// <remarks>
    /// Carried as state rather than threaded through every drawing method because the walk is synchronous and
    /// depth-first: a cell sets it while its own content is drawn and restores it on the way out, so nesting reads
    /// exactly as the tree does. It is the only piece of drawing state that is not a parameter, and it is here because
    /// a run's colour has to be judged against what is behind it rather than against what is behind the pane.
    /// </remarks>
    private Windows.UI.Color? behind;

    /// <summary>Initializes a drawing over the sentences it composes and what it hands a followed link to.</summary>
    /// <param name="words">The sentences the drawing itself needs.</param>
    /// <param name="follow">What a link and the words the message put on it are handed to when it is chosen.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="words" /> or <paramref name="follow" /> is <see langword="null" />.</exception>
    internal MailBodyDrawing(MailBodyWords words, Action<MailBodyLink, string> follow)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(follow);

        this.words = words;
        this.follow = follow;
    }

    /// <summary>Draws the blocks, leaving each picture a place to arrive in.</summary>
    /// <param name="blocks">The blocks the deployment reduced the message to.</param>
    /// <returns>The element the pane hosts.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="blocks" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Nothing is resolved here, which is what makes the message readable while its pictures are being decided rather
    /// than after: a remote picture the reader consented to is fetched from somebody else's server, and a server that
    /// answers slowly or never would otherwise hold the whole message — its words included — off the screen for as long
    /// as the platform waits. The caller attaches this and then calls <see cref="FillPicturesAsync" />.
    /// </remarks>
    internal UIElement Draw(IReadOnlyList<MailBodyBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        return this.Stack(blocks, depth: 0);
    }

    /// <summary>Fills in whichever pictures resolve, into the tree the drawing already produced.</summary>
    /// <param name="isCurrent">Answers whether the message this drawing belongs to is still the one being read.</param>
    /// <returns>A task that completes when every picture has been decided, or when the reading moved on.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="isCurrent" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A picture that resolves to nothing leaves what the message said it shows in its place, which is what a reader can
    /// act on. Awaited on the pane's own context, because every one of these continuations ends at the visual tree.
    /// <para>
    /// The window this asks about is a real one and it is here rather than before the loop: each picture is awaited in
    /// turn, so a reader who opens another message part-way through would otherwise have the rest resolved into a tree
    /// nothing is showing — and, for a source they consented to load remotely, still fetched from the sender's server,
    /// disclosing a read of a message they have already left.
    /// </para>
    /// <para>
    /// The yield in front of the question is what makes the question answerable. Resolving a remote source hands back a
    /// bitmap that decodes on its own and completes synchronously, so a document whose pictures are all remote would
    /// otherwise run the whole loop within one turn — with the reading unable to have changed at any point in it, which
    /// is exactly the case the guard is here for.
    /// </para>
    /// </remarks>
    internal async Task FillPicturesAsync(Func<bool> isCurrent)
    {
        ArgumentNullException.ThrowIfNull(isCurrent);

        foreach (var pending in this.pictures)
        {
            await Task.Yield();

            if (!isCurrent())
            {
                return;
            }

            await Fill(pending);
        }
    }

    /// <summary>Reads a resource the styles dictionary authored, or nothing where it is absent.</summary>
    private static object? Authored(string key) =>
        Application.Current?.Resources is { } resources && resources.TryGetValue(key, out var value)
            ? value
            : null;

    private static void Apply(FrameworkElement element, string styleKey)
    {
        if (Authored(styleKey) is Style style)
        {
            element.Style = style;
        }
    }

    /// <summary>Reads a colour the deployment wrote as <c>#rrggbb</c>, or nothing where it wrote something else.</summary>
    private static Windows.UI.Color? Shade(string? notation)
    {
        if (notation is not { Length: 7 } || notation[0] != '#')
        {
            return null;
        }

        var digits = notation.AsSpan(1);

        // AllowHexSpecifier alone, because HexNumber also admits surrounding whitespace inside the pair.
        return byte.TryParse(digits[..2], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var red)
            && byte.TryParse(digits[2..4], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var green)
            && byte.TryParse(digits[4..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var blue)
                ? Windows.UI.Color.FromArgb(byte.MaxValue, red, green, blue)
                : null;
    }

    /// <summary>Reads the pane's own colour behind a resource key, or nothing where the dictionary is not loaded.</summary>
    private static Windows.UI.Color? Theme(string key) =>
        Authored(key) is SolidColorBrush brush ? brush.Color : null;

    /// <summary>Paints a sender's text colour, or nothing where drawing it would cost the reader the words.</summary>
    /// <remarks>
    /// A message is written for the background its author had in mind, and most mail was written for white paper. Taken
    /// verbatim, <c>color:#000000</c> is black text on a dark theme's surface and <c>color:#ffffff</c> is nothing at all
    /// on a light one — so the colour is honoured only while it stays legible against the surface this pane actually
    /// draws on, and gives way to the theme's own ink otherwise. Nothing is adjusted towards legibility: a colour is
    /// the sender's or it is the theme's, because a shifted colour is a third thing neither of them chose.
    /// </remarks>
    private SolidColorBrush? Ink(string? notation)
    {
        if (Shade(notation) is not { } ink)
        {
            return null;
        }

        return (this.behind ?? Theme(SurfaceBrushKey)) is { } ground && !Legible(ink, ground)
            ? null
            : new SolidColorBrush(ink);
    }

    /// <summary>Paints a sender's cell background, or nothing where the pane's own text would not read on it.</summary>
    private static SolidColorBrush? Ground(string? notation)
    {
        if (Shade(notation) is not { } ground)
        {
            return null;
        }

        return Theme(InkBrushKey) is { } ink && !Legible(ink, ground)
            ? null
            : new SolidColorBrush(ground);
    }

    /// <summary>Says whether words of one colour can be read on the other, which is the whole of the floor.</summary>
    /// <remarks>
    /// WCAG's AA ratio for body text. Stated as a predicate rather than inline at each guard so the two guards cannot
    /// come to mean different things, and so the floor itself is decidable without a loaded theme.
    /// </remarks>
    internal static bool Legible(Windows.UI.Color ink, Windows.UI.Color ground) =>
        Contrast(ink, ground) >= MinimumContrast;

    /// <summary>The contrast ratio between two colours, as WCAG defines it.</summary>
    private static double Contrast(Windows.UI.Color first, Windows.UI.Color second)
    {
        var brighter = Math.Max(Luminance(first), Luminance(second));
        var darker = Math.Min(Luminance(first), Luminance(second));

        return (brighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(Windows.UI.Color colour) =>
        (0.2126 * Channel(colour.R)) + (0.7152 * Channel(colour.G)) + (0.0722 * Channel(colour.B));

    private static double Channel(byte value)
    {
        var share = value / 255.0;

        return share <= 0.03928 ? share / 12.92 : Math.Pow((share + 0.055) / 1.055, 2.4);
    }

    private static TextAlignment Across(MailBodyAlignment alignment) => alignment switch
    {
        MailBodyAlignment.Start => TextAlignment.Left,
        MailBodyAlignment.Center => TextAlignment.Center,
        MailBodyAlignment.End => TextAlignment.Right,
        MailBodyAlignment.Justify => TextAlignment.Justify,
        _ => TextAlignment.DetectFromContent,
    };

    private static HorizontalAlignment Sits(MailBodyAlignment alignment) => alignment switch
    {
        MailBodyAlignment.Center => HorizontalAlignment.Center,
        MailBodyAlignment.End => HorizontalAlignment.Right,
        _ => HorizontalAlignment.Left,
    };

    /// <summary>Puts a picture in place, or what the message said it shows where nothing could be drawn.</summary>
    private static async Task Fill(PendingPicture pending)
    {
        ImageSource? resolved;

        try
        {
            resolved = await MailBodyImages.ResolveAsync(pending.Picture.Source);
        }
#pragma warning disable CA1031 // The picture is a stranger's, and every way it can fail ends the same way here.
        catch (Exception)
        {
            resolved = null;
        }
#pragma warning restore CA1031

        if (resolved is null)
        {
            pending.Element.Visibility = Visibility.Collapsed;
            pending.Description.Visibility = Visibility.Visible;

            return;
        }

        pending.Element.Source = resolved;
    }

    private static void Break(InlineCollection inlines, string text, Action<Run> dress)
    {
        var lines = text.Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0)
            {
                inlines.Add(new LineBreak());
            }

            if (lines[index].Length == 0)
            {
                continue;
            }

            var run = new Run { Text = lines[index] };
            dress(run);
            inlines.Add(run);
        }
    }

    private void Dress(Run run, MailBodyRun content)
    {
        if (content.Emphasis.HasFlag(MailBodyEmphasis.Bold))
        {
            run.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        }

        if (content.Emphasis.HasFlag(MailBodyEmphasis.Italic))
        {
            run.FontStyle = Windows.UI.Text.FontStyle.Italic;
        }

        if (content.Emphasis.HasFlag(MailBodyEmphasis.Monospace) && Authored(MonospaceFontKey) is FontFamily face)
        {
            run.FontFamily = face;
        }

        var decorations = Windows.UI.Text.TextDecorations.None;

        if (content.Emphasis.HasFlag(MailBodyEmphasis.Underline))
        {
            decorations |= Windows.UI.Text.TextDecorations.Underline;
        }

        if (content.Emphasis.HasFlag(MailBodyEmphasis.Strikethrough))
        {
            decorations |= Windows.UI.Text.TextDecorations.Strikethrough;
        }

        run.TextDecorations = decorations;

        if (this.Ink(content.Foreground) is { } ink)
        {
            run.Foreground = ink;
        }
    }

    private StackPanel Stack(IReadOnlyList<MailBodyBlock> blocks, int depth)
    {
        var panel = new StackPanel();

        if (Authored(BlockSpacingKey) is double spacing)
        {
            panel.Spacing = spacing;
        }

        if (depth > MaximumDepth)
        {
            panel.Children.Add(this.Unsupported());

            return panel;
        }

        foreach (var block in blocks)
        {
            panel.Children.Add(this.Draw(block, depth));
        }

        return panel;
    }

    private UIElement Draw(MailBodyBlock block, int depth) => block switch
    {
        MailBodyParagraphBlock paragraph => this.Paragraph(paragraph),
        MailBodyHeadingBlock heading => this.Heading(heading),
        MailBodyListBlock list => this.List(list, depth),
        MailBodyQuoteBlock quote => this.Quote(quote, depth),
        MailBodyTableBlock table => this.Table(table, depth),
        MailBodyImageBlock picture => this.Picture(picture),
        MailBodyPreformattedBlock preformatted => Preformatted(preformatted),
        MailBodySeparatorBlock => Separator(),
        _ => this.Unsupported(),
    };

    private TextBlock Paragraph(MailBodyParagraphBlock paragraph)
    {
        var text = this.Text(paragraph.Content);
        text.TextAlignment = Across(paragraph.Alignment);
        Apply(text, ParagraphStyleKey);

        return text;
    }

    private TextBlock Heading(MailBodyHeadingBlock heading)
    {
        var text = this.Text(heading.Content);
        text.TextAlignment = Across(heading.Alignment);
        Apply(text, $"MailBodyHeading{Math.Clamp(heading.Level, 1, 6)}Style");

        // The level is what a screen reader announces the shape of a message from, and nothing about the typography
        // says it — so it is stated for the automation peer rather than inferred from how large the words are drawn.
        AutomationProperties.SetLevel(text, Math.Clamp(heading.Level, 1, 6));

        return text;
    }

    private TextBlock Text(IReadOnlyList<MailBodyRun> content)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };

        foreach (var run in content)
        {
            if (run.Link is { } link)
            {
                this.Anchor(text.Inlines, run, link);

                continue;
            }

            Break(text.Inlines, run.Text, dressed => this.Dress(dressed, run));
        }

        return text;
    }

    /// <summary>Draws one run the message made a link, keyboard-reachable and answering before it is followed.</summary>
    private void Anchor(InlineCollection inlines, MailBodyRun run, MailBodyLink link)
    {
        var anchor = new Hyperlink();
        Break(anchor.Inlines, run.Text, dressed => this.Dress(dressed, run));

        // No NavigateUri: setting it would have the platform open the target on a click, which is the one thing this
        // pane may not do. The reader is shown where the link goes and answers, and the pane opens it from there.
        anchor.Click += (_, _) => this.follow(link, run.Text);

        // What a screen reader announces is where the link goes rather than the words the sender chose for it, since
        // those words are exactly what a deceptive link controls.
        AutomationProperties.SetName(anchor, link.Place);
        inlines.Add(anchor);
    }

    private StackPanel List(MailBodyListBlock list, int depth)
    {
        var panel = new StackPanel();

        if (Authored(BlockSpacingKey) is double spacing)
        {
            panel.Spacing = spacing;
        }

        for (var index = 0; index < list.Items.Count; index++)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var marker = new TextBlock
            {
                Text = list.Ordered
                    ? string.Create(CultureInfo.CurrentCulture, $"{index + 1}. ")
                    : "• ",
            };
            Apply(marker, ParagraphStyleKey);

            var content = this.Stack(list.Items[index].Blocks, depth + 1);
            Grid.SetColumn(content, 1);

            row.Children.Add(marker);
            row.Children.Add(content);
            panel.Children.Add(row);
        }

        return panel;
    }

    private Border Quote(MailBodyQuoteBlock quote, int depth)
    {
        var border = new Border { Child = this.Stack(quote.Blocks, depth + 1) };
        Apply(border, QuoteStyleKey);

        // The depth is said rather than drawn ever deeper: three replies of history would otherwise indent a message
        // off the pane, and how deep a quotation is, is what a reader wants rather than how far right it sits.
        AutomationProperties.SetLevel(border, Math.Max(quote.Depth, 1));

        return border;
    }

    private Grid Table(MailBodyTableBlock table, int depth)
    {
        var grid = new Grid();
        var columns = Math.Max(table.Columns.Count, 1);

        foreach (var column in table.Columns)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(column.WidthShare is > 0 ? column.WidthShare.Value : 1d, GridUnitType.Star),
            });
        }

        if (grid.ColumnDefinitions.Count == 0)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var taken = new List<bool[]>();

        for (var row = 0; row < table.Rows.Count; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            taken.Add(new bool[columns]);
        }

        for (var row = 0; row < table.Rows.Count; row++)
        {
            var column = 0;

            foreach (var cell in table.Rows[row].Cells)
            {
                while (column < columns && taken[row][column])
                {
                    column++;
                }

                if (column >= columns)
                {
                    break;
                }

                var spanned = Math.Clamp(cell.ColumnSpan, 1, columns - column);
                var stacked = Math.Clamp(cell.RowSpan, 1, table.Rows.Count - row);

                for (var down = row; down < row + stacked; down++)
                {
                    for (var across = column; across < column + spanned; across++)
                    {
                        taken[down][across] = true;
                    }
                }

                var drawn = this.Cell(cell, table.Rows[row].IsHeader, depth);
                Grid.SetRow(drawn, row);
                Grid.SetColumn(drawn, column);
                Grid.SetColumnSpan(drawn, spanned);
                Grid.SetRowSpan(drawn, stacked);
                grid.Children.Add(drawn);

                column += spanned;
            }
        }

        return grid;
    }

    /// <summary>Draws one cell, with what it paints behind the words drawn inside it.</summary>
    /// <remarks>
    /// The two colour guards used to answer independently — a run against the pane's surface, a cell against the
    /// theme's ink — and neither was told what the other admitted, so a sender choosing one colour for the cell and the
    /// same colour for its text cleared both and drew words at a contrast of one against their own background. The
    /// background this cell actually painted is what the runs inside it are now judged against.
    /// </remarks>
    private Border Cell(MailBodyTableCell cell, bool isHeader, int depth)
    {
        var ground = Ground(cell.Background);
        var outer = this.behind;

        this.behind = ground?.Color ?? outer;

        var content = this.Stack(cell.Blocks, depth + 1);

        this.behind = outer;

        content.HorizontalAlignment = Sits(cell.Alignment);

        var border = new Border { Child = content };
        Apply(border, isHeader ? HeaderCellStyleKey : CellStyleKey);

        if (ground is not null)
        {
            border.Background = ground;
        }

        return border;
    }

    private StackPanel Picture(MailBodyImageBlock block)
    {
        var panel = new StackPanel { HorizontalAlignment = Sits(block.Alignment) };

        var element = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        // The sender's dimensions shape the picture and never size it against the window: the maximum is the width the
        // pane has, so nothing a message says can push the reading column wider than it is.
        if (block.Image.Width is > 0)
        {
            element.MaxWidth = block.Image.Width.Value;
        }

        if (block.Image.Height is > 0)
        {
            element.MaxHeight = block.Image.Height.Value;
        }

        var described = block.Image.AlternativeText is { Length: > 0 } stated
            ? stated
            : this.words.UndrawnImage;
        AutomationProperties.SetName(element, described);

        var description = new TextBlock
        {
            Text = described,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        Apply(description, UnsupportedStyleKey);

        panel.Children.Add(element);
        panel.Children.Add(description);

        if (block.Link is { } link)
        {
            var caption = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var anchor = new Hyperlink();
            anchor.Inlines.Add(new Run { Text = link.Place });
            anchor.Click += (_, _) => this.follow(link, described);
            AutomationProperties.SetName(anchor, link.Place);
            caption.Inlines.Add(anchor);
            Apply(caption, ParagraphStyleKey);
            panel.Children.Add(caption);
        }

        this.pictures.Add(new PendingPicture(element, description, block.Image));

        return panel;
    }

    private TextBlock Unsupported()
    {
        var text = new TextBlock { Text = this.words.UnsupportedBlock, TextWrapping = TextWrapping.Wrap };
        Apply(text, UnsupportedStyleKey);

        return text;
    }

    private static ScrollViewer Preformatted(MailBodyPreformattedBlock preformatted)
    {
        var text = new TextBlock
        {
            Text = preformatted.Text,
            TextWrapping = TextWrapping.NoWrap,
            IsTextSelectionEnabled = true,
        };
        Apply(text, PreformattedStyleKey);

        // Scrolled rather than wrapped: re-wrapping a code sample or a diff would change what the message says, which
        // is the whole reason this arrives as a block of its own.
        return new ScrollViewer
        {
            Content = text,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
        };
    }

    private static Border Separator()
    {
        var rule = new Border();
        Apply(rule, SeparatorStyleKey);

        return rule;
    }

    /// <summary>A picture drawn into the tree whose source has not been resolved yet.</summary>
    private sealed record PendingPicture(Image Element, TextBlock Description, MailBodyImage Picture);
}
