// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Presentation;

/// <summary>The screen that asks which MailFathom deployment this client reaches.</summary>
/// <remarks>It is the first screen a fresh installation reaches and the one somebody returns to in order to point the client elsewhere; which of the two is happening is the address already in the box, and nothing here needs to know.</remarks>
internal sealed partial class ConnectPage : Page
{
    /// <summary>Initializes the page.</summary>
    public ConnectPage()
    {
        this.InitializeComponent();
    }
}
