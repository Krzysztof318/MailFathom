// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail;

/// <summary>Resolves the key record a signing domain publishes for one DKIM selector.</summary>
/// <remarks>
/// <para>
/// The port exists so that the one place MailFathom reaches the network to judge a sender is a named, replaceable
/// seam rather than a call buried in a parser. What crosses it is a selector, a domain, and the record's text —
/// no resolver type, no MIME type, and no cryptographic type, so an implementation may answer from DNS, from a
/// cache, or from a fixture without any of that reaching the verification above it.
/// </para>
/// <para>
/// <b>Every implementation is bounded and answers rather than raises.</b> A record that does not exist, a nameserver
/// that cannot be reached, and a lookup that outlives its deadline are all ordinary outcomes of asking a stranger's
/// nameserver a question, and each of them answers with no record. Extraction runs over whatever arrives in a mailbox,
/// so a resolver that threw or stalled would turn somebody else's DNS trouble into a folder that never advances past
/// one message.
/// </para>
/// <para>
/// What is disclosed by a call is <c>&lt;selector&gt;._domainkey.&lt;domain&gt;</c> and nothing else: a low-cardinality
/// name the signing domain published in order to be asked for, carrying nothing about this message, this mailbox, or
/// this recipient. It is resolved when a message is stored rather than when one is read, so no lookup is ever
/// correlated with somebody opening their mail.
/// </para>
/// </remarks>
public interface IDkimPublicKeyRecordResolver
{
    /// <summary>Resolves what a signing domain publishes for one selector.</summary>
    /// <param name="selector">The selector a signature named, which is the label beneath <c>_domainkey</c>.</param>
    /// <param name="signingDomain">The domain a signature was made for.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The record's text, or <see langword="null" /> where none could be resolved.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="selector" /> or <paramref name="signingDomain" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Cancellation through <paramref name="cancellationToken" /> is the caller giving up and propagates as it does
    /// anywhere else. A deadline the implementation applies to the lookup itself is not: it answers with no record,
    /// because a slow nameserver is a fact about the network rather than a decision the caller took.
    /// </remarks>
    Task<string?> ResolveAsync(string selector, string signingDomain, CancellationToken cancellationToken);
}
