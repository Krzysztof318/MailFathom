// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend.Timeline;

/// <summary>One page of the message list, and the cursors that continue it at either end.</summary>
/// <param name="Emails">The rows, in the order the request asked the list to be sorted in.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end of the list.</param>
/// <param name="PreviousCursor">The cursor the preceding page is asked with, or <see langword="null" /> at the beginning of the list.</param>
/// <param name="PageSize">How many rows the read ran under, which is what the request asked for or the default it took.</param>
/// <remarks>
/// <para>
/// Both cursors are holdable and neither is a position the deployment remembers, so a client may keep one while the
/// screen is closed and continue from it afterwards — against a deployment that has restarted in between. An absent one
/// is that end of the list having been reached rather than a hint to ask again.
/// </para>
/// <para>
/// A page that came back empty carries no cursor at either end, because a cursor names a row and there is none. A
/// client that reached an end keeps whatever it was holding rather than being handed something back.
/// </para>
/// </remarks>
public sealed record DeploymentMailTimelinePage(
    IReadOnlyList<DeploymentMailMessage> Emails,
    string? NextCursor,
    string? PreviousCursor,
    int PageSize)
{
    /// <summary>A page that was never read, which is what a list holds before it has asked for anything.</summary>
    public static DeploymentMailTimelinePage Nothing { get; } = new([], NextCursor: null, PreviousCursor: null, 0);

    /// <summary>Gets the rows, reading a document that named none as a page that holds nothing.</summary>
    public IReadOnlyList<DeploymentMailMessage> Rows => this.Emails ?? [];
}
