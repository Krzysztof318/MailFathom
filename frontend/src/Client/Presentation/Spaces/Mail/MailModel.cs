// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Session;

namespace MailFathom.Client.Presentation.Spaces.Mail;

/// <summary>The model behind <see cref="MailPage"/>: the space correspondence is read in.</summary>
/// <remarks>
/// <para>
/// Which mailboxes there are, how current each copy is, and which folder is being read are not this space's to answer.
/// They are one tree, that tree is the client's scope selector, and the frame renders it — because the list here, the
/// search, and the field a question is composed in are all about wherever the tree says somebody is. A copy of the
/// mailboxes drawn here beside it would be the same answer twice, the second already stale relative to the first.
/// </para>
/// <para>
/// What is left is the space's own reading of the session: whether correspondence may be put in front of this caller
/// at all, read here rather than derived from a request the deployment refused.
/// </para>
/// </remarks>
public partial record MailModel
{
    /// <summary>Initializes the space over what decides whether it may be offered.</summary>
    /// <param name="session">What the deployment allows this caller.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    public MailModel(IClientSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        this.WithholdsMail = session.Standing.Select(standing => standing.Withholds(ClientCapability.Mail));
    }

    /// <summary>Whether this session keeps the space correspondence is read in from being put in front of this caller.</summary>
    /// <remarks>
    /// The space's own reading of the session the frame reads, stated as an affirmative for the reason
    /// <see cref="SessionStanding.Withholds" /> gives: a control shown on the absence of an offer would be on the
    /// screen before the session had answered.
    /// </remarks>
    public IFeed<bool> WithholdsMail { get; }
}
