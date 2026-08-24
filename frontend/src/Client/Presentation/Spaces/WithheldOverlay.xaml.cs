// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Presentation.Spaces;

/// <summary>What a space is instead where the session does not offer it to this caller.</summary>
/// <remarks>
/// <para>
/// The frame drops a withheld space from its navigation, so the only way to be looking at one is to have opened on it
/// — which the route's default decides before any session is known. This is what stands there then, over the space
/// rather than instead of it, so that a session nothing has answered for yet is the space as it was rather than a
/// blank page.
/// </para>
/// <para>
/// One control rather than the same four lines in each space, and it takes its answer as a property rather than
/// reaching into the page's <c>DataContext</c> for a name it would have to know — the rule
/// <c>frontend/src/AGENTS.md</c> § <em>XAML first</em> states about a fragment that appears twice.
/// </para>
/// </remarks>
public sealed partial class WithheldOverlay : UserControl
{
    /// <summary>Identifies the <see cref="Withheld"/> property.</summary>
    public static readonly DependencyProperty WithheldProperty = DependencyProperty.Register(
        nameof(Withheld),
        typeof(bool),
        typeof(WithheldOverlay),
        new PropertyMetadata(false));

    /// <summary>Initializes the overlay.</summary>
    public WithheldOverlay()
    {
        this.InitializeComponent();
    }

    /// <summary>Whether the session withholds the space this stands over.</summary>
    /// <remarks>
    /// Stated as the withholding rather than as the offer, so that the default — which is what a binding carries
    /// before the session has answered — leaves this off the screen. A control shown on the absence of an offer would
    /// announce a space as taken away while the fetch that decides it was still running.
    /// </remarks>
    public bool Withheld
    {
        get => (bool)this.GetValue(WithheldProperty);
        set => this.SetValue(WithheldProperty, value);
    }
}
