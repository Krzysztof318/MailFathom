// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Presentation.Spaces;

/// <summary>
/// What a column of a space holds until the space that owns it is built: what it is for, said plainly, rather than a
/// blank rectangle somebody cannot tell from a failure.
/// </summary>
/// <remarks>
/// It exists so the frame can be seen working — a wide window shows two columns and a narrow one shows the primary
/// column alone — while what fills either of them stays each space's own decision. A space that has been built carries
/// its own content and no longer names this control.
/// </remarks>
public sealed partial class SpacePlaceholder : UserControl
{
    /// <summary>Identifies the <see cref="Heading"/> property.</summary>
    public static readonly DependencyProperty HeadingProperty = DependencyProperty.Register(
        nameof(Heading),
        typeof(string),
        typeof(SpacePlaceholder),
        new PropertyMetadata(string.Empty));

    /// <summary>Identifies the <see cref="Detail"/> property.</summary>
    public static readonly DependencyProperty DetailProperty = DependencyProperty.Register(
        nameof(Detail),
        typeof(string),
        typeof(SpacePlaceholder),
        new PropertyMetadata(string.Empty));

    /// <summary>Initializes the placeholder.</summary>
    public SpacePlaceholder()
    {
        this.InitializeComponent();
    }

    /// <summary>What this column is, in a few words.</summary>
    public string Heading
    {
        get => (string)this.GetValue(HeadingProperty);
        set => this.SetValue(HeadingProperty, value);
    }

    /// <summary>What will be here, in one sentence.</summary>
    public string Detail
    {
        get => (string)this.GetValue(DetailProperty);
        set => this.SetValue(DetailProperty, value);
    }
}
