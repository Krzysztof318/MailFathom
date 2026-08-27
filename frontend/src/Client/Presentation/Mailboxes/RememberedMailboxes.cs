// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using MailFathom.Client.Presentation.Workspace;

namespace MailFathom.Client.Presentation.Mailboxes;

/// <summary>Where somebody left the mailbox tree, which is what starting the client again opens it on.</summary>
/// <param name="Scope">The place the tree was narrowed to, without whatever was selected inside it.</param>
/// <param name="Expanded">The keys of the rows whose contents were being shown.</param>
/// <remarks>
/// <para>
/// The two are remembered together because they are one arrangement: an expansion restored without the selection would
/// open the tree somewhere nobody is, and a selection restored without the expansion would put the selected row out of
/// sight beneath a collapsed mailbox.
/// </para>
/// <para>
/// What is inside the scope is deliberately not part of this. A selection of messages is what somebody was reading a
/// moment ago rather than where they work, and the mail it names may not be in the copy by the time the client is
/// started again.
/// </para>
/// </remarks>
public sealed record RememberedMailboxes(WorkspaceScope Scope, IImmutableSet<string> Expanded)
{
    /// <summary>Nothing remembered, which is what a first run and an unreadable answer both read as.</summary>
    public static RememberedMailboxes Nothing { get; } =
        new(WorkspaceScope.Everything, ImmutableHashSet<string>.Empty);
}
