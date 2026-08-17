// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails.Authentication;

namespace MailFathom.Application.Emails.Summaries;

/// <summary>The two conclusions a stored email carries about the author it displays, read back as they were stored.</summary>
/// <remarks>
/// <para>
/// The pair is never collapsed into one value. <see cref="AuthorAuthentication" /> is what the receiving mail server
/// established about the displayed author, and <see cref="DeploymentTrust" /> is whether this deployment recognizes the
/// author it established — a fact about the message beside a decision about a list. An authenticated author nobody has
/// named is the ordinary state of legitimate mail and carries the same trust value as an author whose authentication
/// failed, so a reader that had only one of the two could not tell those apart.
/// </para>
/// <para>
/// It is read rather than reached. Nothing on a read path evaluates a policy, resolves DNS, or re-reads a header to
/// produce it: the values are the columns extraction wrote, and a message extraction never reached carries
/// <see cref="NotEstablished" /> because that is what its row holds.
/// </para>
/// </remarks>
public sealed record SenderVerification
{
    /// <summary>Gets the pair a row carries where nothing has established an author and no policy has judged one.</summary>
    /// <remarks>
    /// It is the stored default rather than an absence, which is what mail stored before the columns existed reads as
    /// until a re-derivation reaches it. Publishing it as it stands is deliberate: inventing a third state for
    /// "unfilled" would say something about the message that nothing established.
    /// </remarks>
    public static SenderVerification NotEstablished { get; } = new()
    {
        AuthorAuthentication = AuthorAuthenticationOutcome.NotEstablished,
        DeploymentTrust = SenderTrustLevel.Unknown,
    };

    /// <summary>Gets what the receiving mail server established about the author the message displays.</summary>
    public required AuthorAuthenticationOutcome AuthorAuthentication { get; init; }

    /// <summary>Gets whether this deployment recognizes the author, which is its own classification and not a check.</summary>
    public required SenderTrustLevel DeploymentTrust { get; init; }
}
