// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Presentation;

/// <summary>The routes this application is reachable by, named once.</summary>
/// <remarks>
/// A route is a string in three places — where it is registered, where a model navigates to it, and where a XAML
/// <c>Navigation.Request</c> names it — and only the first two can be held together by a compiler. A route renamed in
/// one of those and not the others fails at run time as a navigation that does nothing, which is why the two C# ends
/// read from here; the XAML end states the same word and is what a reader of the page sees.
/// </remarks>
internal static class ClientRoutes
{
    /// <summary>The frame the three spaces are shown inside, which is where a client pointed at a deployment starts.</summary>
    internal const string Workspace = "Workspace";

    /// <summary>The space a question is asked in, and the one the frame opens on.</summary>
    internal const string Discover = "Discover";

    /// <summary>The space correspondence is read in.</summary>
    internal const string Mail = "Mail";

    /// <summary>The phone-width screen that follows the selected message's conversation.</summary>
    internal const string MailThread = "MailThread";

    /// <summary>The phone-width screen that reads one message of the open conversation.</summary>
    internal const string MailMessage = "MailMessage";

    /// <summary>The space a thread being worked through is followed in.</summary>
    internal const string Cases = "Cases";

    /// <summary>
    /// The screen reached from the frame rather than named among the spaces, because it is not one. It sits beside the
    /// frame rather than inside it, which is what makes going to it a screen somebody comes back from.
    /// </summary>
    internal const string Settings = "Settings";

    /// <summary>The screen that asks which deployment this client reaches, which is where one pointed nowhere starts.</summary>
    internal const string Connect = "Connect";

    /// <summary>
    /// The screen that asks who somebody is on that deployment, which is where a client pointed somewhere with no
    /// usable credential starts and where one whose session ended returns.
    /// </summary>
    internal const string SignIn = "SignIn";
}
