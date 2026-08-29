// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Presentation;

/// <summary>
/// The control every route is nested inside. It holds the content area navigation writes into, and nothing else: a
/// screen belongs to a route rather than to the shell.
/// </summary>
public sealed partial class Shell : UserControl, IContentControlProvider
{
    /// <summary>Initializes the shell.</summary>
    public Shell()
    {
        this.InitializeComponent();
    }

    /// <inheritdoc />
    public ContentControl ContentControl => this.RouteContent;
}
