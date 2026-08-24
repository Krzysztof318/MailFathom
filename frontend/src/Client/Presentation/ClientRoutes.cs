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
    /// <summary>The application itself, which is where a client pointed at a deployment starts.</summary>
    internal const string Main = "Main";

    /// <summary>The screen that asks which deployment this client reaches, which is where one pointed nowhere starts.</summary>
    internal const string Connect = "Connect";
}
