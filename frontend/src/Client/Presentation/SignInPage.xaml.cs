// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Presentation;

/// <summary>The screen that asks who somebody is on the deployment this client is pointed at.</summary>
/// <remarks>The screen a start with an address and no usable credential opens on, and the one a session that ended returns to — whether it ended because somebody signed out or because the deployment stopped accepting what it had.</remarks>
internal sealed partial class SignInPage : Page
{
    /// <summary>Initializes the page.</summary>
    public SignInPage()
    {
        this.InitializeComponent();
    }
}
