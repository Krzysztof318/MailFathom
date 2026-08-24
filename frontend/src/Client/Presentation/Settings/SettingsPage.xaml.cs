// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Presentation.Settings;

/// <summary>
/// What a reader can decide about the application itself: which language it is read in, and which theme it is shown
/// in. It names the build it is running beside them, which is what somebody reporting a problem is asked for.
/// </summary>
public sealed partial class SettingsPage : Page
{
    /// <summary>Initializes the page.</summary>
    public SettingsPage()
    {
        this.InitializeComponent();
    }
}
