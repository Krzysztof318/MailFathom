// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>Reads one message's sender-authentication verdict out of the headers a receiving server wrote.</summary>
/// <remarks>
/// <para>
/// This is the whole of MailFathom's reasoning about who sent a message, and it is deliberately small. It selects the
/// one header the account's configured authority produced, reads the identities that header states, and records them.
/// It resolves no DNS, verifies no signature, evaluates no policy, and reads no <c>Received</c> chain — a message's
/// authenticity was decided by the server that observed the connection, and nothing after delivery can decide it again.
/// </para>
/// <para>
/// The selection rule is the security property rather than a detail of it. Every other <c>Authentication-Results</c>
/// header is ignored whatever it says, because anything upstream can write one; and where the trusted server wrote
/// several, the topmost is taken, since a receiving server adds its own above whatever it found and may leave a forged
/// one below.
/// </para>
/// <para>
/// What it hands the verdict is every identity the trusted header reported as passing rather than one per method, so
/// that <see cref="SenderAuthentication.AuthorAuthentication" /> is decided against all of them. A message signed by a
/// delivery provider as well as by its author carries two verified signatures, and which of the two a server happened
/// to list first is not something the displayed author may depend on.
/// </para>
/// </remarks>
public static class SenderAuthenticationReading
{
    /// <summary>RFC 8601's method token for a DKIM signature verification.</summary>
    private const string DkimMethod = "dkim";

    /// <summary>RFC 8601's method token for an SPF evaluation.</summary>
    private const string SpfMethod = "spf";

    /// <summary>RFC 8601's method token for a DMARC evaluation.</summary>
    private const string DmarcMethod = "dmarc";

    /// <summary>The result token every method uses for an outcome that held.</summary>
    private const string PassResult = "pass";

    /// <summary>The result token every method uses for a check that had nothing to evaluate.</summary>
    private const string NoneResult = "none";

    /// <summary>The <c>ptype</c> under which a header-derived property is written.</summary>
    private const string HeaderPropertyType = "header";

    /// <summary>The <c>ptype</c> under which an envelope-derived property is written.</summary>
    private const string SmtpPropertyType = "smtp";

    /// <summary>The property naming the domain a DKIM signature was made for.</summary>
    private const string DkimDomainProperty = "d";

    /// <summary>The property naming the envelope sender an SPF check evaluated.</summary>
    private const string SpfMailFromProperty = "mailfrom";

    /// <summary>Reads the verdict one message's headers support.</summary>
    /// <param name="headers">Every <c>Authentication-Results</c> header the message carried, topmost first.</param>
    /// <param name="authority">The one server this account believes, which may name none.</param>
    /// <param name="displayedSenderAddress">The address the message's <c>From</c> header wrote, where it wrote one.</param>
    /// <returns>The verdict, which is the not-established one wherever nothing trusted could be read.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="headers" /> is <see langword="null" />.</exception>
    public static SenderAuthentication Read(
        IReadOnlyList<AuthenticationResultsHeader> headers,
        TrustedAuthenticationAuthority authority,
        string? displayedSenderAddress)
    {
        ArgumentNullException.ThrowIfNull(headers);

        var fromDomain = SenderDomain.TryCreateFromMailbox(displayedSenderAddress, out var displayed)
            ? displayed
            : default(SenderDomain?);

        // An account that trusts no server believes no header, so the search below is skipped rather than run against
        // an authority that matches nothing. The two paths reach the same verdict; separating them is what keeps the
        // reason readable when a deployment finds every message unauthenticated.
        if (FindTrustedHeader(headers, authority) is not { } trustedHeader)
        {
            return SenderAuthentication.NotEstablished(fromDomain);
        }

        var dmarc = ReadDmarcOutcome(trustedHeader.Methods);
        var dkimDomains = ReadVerifiedDkimDomains(trustedHeader.Methods);
        var spfDomains = ReadPassedSpfDomains(trustedHeader.Methods);

        if (dkimDomains.Count > 0 || spfDomains.Count > 0)
        {
            return SenderAuthentication.Authenticated(dkimDomains, spfDomains, fromDomain, dmarc);
        }

        return WasIdentityAttempted(trustedHeader.Methods)
            ? SenderAuthentication.Failed(fromDomain, dmarc)
            : SenderAuthentication.NotEstablished(fromDomain, dmarc);
    }

    /// <summary>Answers whether the account's trusted server wrote a statement about this message at all.</summary>
    /// <param name="headers">Every <c>Authentication-Results</c> header the message carried, topmost first.</param>
    /// <param name="authority">The one server this account believes, which may name none.</param>
    /// <returns><see langword="true" /> when a header this account trusts was found; otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="headers" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// This is the condition local verification is a fallback for, and it deliberately asks whether a trusted statement
    /// <em>exists</em> rather than whether it established anything. A server that evaluated the message and reported a
    /// failure has spoken about it with network context nothing here can recover, so verifying the same message again
    /// locally would put a second verdict of a different provenance beside the first — which is exactly what recording
    /// only one of them, from the stronger position, avoids.
    /// </remarks>
    public static bool FindsTrustedStatement(
        IReadOnlyList<AuthenticationResultsHeader> headers,
        TrustedAuthenticationAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(headers);

        return FindTrustedHeader(headers, authority) is not null;
    }

    /// <summary>Selects the topmost header the account's trusted server produced, or none where it wrote nothing.</summary>
    private static AuthenticationResultsHeader? FindTrustedHeader(
        IReadOnlyList<AuthenticationResultsHeader> headers,
        TrustedAuthenticationAuthority authority) =>
        authority.NamesAServer
            ? headers.FirstOrDefault(header => authority.Produced(header.AuthorityIdentifier))
            : null;

    /// <summary>Reads the domain of every DKIM signature the server verified, in header order.</summary>
    /// <remarks>
    /// A message may carry several signatures and a server states one outcome per signature, so a message signed by a
    /// delivery provider as well as by its author has two that verified. All of them are read, because the author is
    /// established by an identity being present rather than by it being listed first: taking one and discarding the rest
    /// would let an unrelated signature hide the one belonging to the displayed author.
    /// </remarks>
    private static IReadOnlyList<SenderDomain> ReadVerifiedDkimDomains(
        IReadOnlyList<ReportedAuthenticationMethod> methods) =>
        PassedDomains(methods, DkimMethod, HeaderPropertyType, DkimDomainProperty, TryReadDomain);

    /// <summary>Reads the envelope-sender domain of every SPF check that passed, in header order.</summary>
    private static IReadOnlyList<SenderDomain> ReadPassedSpfDomains(
        IReadOnlyList<ReportedAuthenticationMethod> methods) =>
        PassedDomains(methods, SpfMethod, SmtpPropertyType, SpfMailFromProperty, TryReadMailboxDomain);

    /// <summary>Reads every usable domain one method's passing outcomes named, in header order.</summary>
    /// <remarks>
    /// An outcome that passed while naming no usable domain contributes nothing rather than standing in for one. There
    /// is no identity to record for it, and inventing one from the displayed sender is exactly the substitution this
    /// whole reading exists to refuse.
    /// </remarks>
    private static IReadOnlyList<SenderDomain> PassedDomains(
        IReadOnlyList<ReportedAuthenticationMethod> methods,
        string method,
        string propertyType,
        string propertyName,
        Func<string, SenderDomain?> readDomain) =>
        [.. methods
            .Where(candidate => Is(candidate.Method, method) && Is(candidate.Result, PassResult))
            .SelectMany(static candidate => candidate.Properties)
            .Where(property => Is(property.Type, propertyType) && Is(property.Name, propertyName))
            .Select(property => readDomain(property.Value))
            .OfType<SenderDomain>()];

    /// <summary>Reads a property written as a bare domain, which is the form a DKIM signing domain takes.</summary>
    private static SenderDomain? TryReadDomain(string value) =>
        SenderDomain.TryCreate(value, out var domain) ? domain : null;

    /// <summary>Reads a property written as a mailbox, which is the form an SPF envelope sender takes.</summary>
    private static SenderDomain? TryReadMailboxDomain(string value) =>
        SenderDomain.TryCreateFromMailbox(value, out var domain) ? domain : null;

    /// <summary>Answers whether the server checked an identity at all, which is what separates a failure from silence.</summary>
    /// <remarks>
    /// Every result token except <c>pass</c> and <c>none</c> is an attempt that did not hold — <c>fail</c>,
    /// <c>softfail</c>, <c>neutral</c>, <c>policy</c>, and both error results. They are read as one because the verdict
    /// distinguishes what was established from what was not, and none of them establishes anything; a reader wanting the
    /// server's exact wording has the stored raw MIME the verdict is re-derivable from.
    /// </remarks>
    private static bool WasIdentityAttempted(IReadOnlyList<ReportedAuthenticationMethod> methods) =>
        methods.Any(candidate =>
            (Is(candidate.Method, DkimMethod) || Is(candidate.Method, SpfMethod))
            && !string.IsNullOrWhiteSpace(candidate.Result)
            && !Is(candidate.Result, PassResult)
            && !Is(candidate.Result, NoneResult));

    /// <summary>Reads what the server reported for DMARC, or that it reported nothing.</summary>
    private static DmarcOutcome ReadDmarcOutcome(IReadOnlyList<ReportedAuthenticationMethod> methods) =>
        methods
            .Where(candidate => Is(candidate.Method, DmarcMethod))
            .Select(static candidate => candidate.Result?.Trim().ToUpperInvariant() switch
            {
                "PASS" => DmarcOutcome.Pass,
                "FAIL" => DmarcOutcome.Fail,
                "NONE" => DmarcOutcome.NoPolicyPublished,
                "TEMPERROR" => DmarcOutcome.TemporaryError,
                "PERMERROR" => DmarcOutcome.PermanentError,
                _ => DmarcOutcome.NotReported,
            })
            .FirstOrDefault();

    /// <summary>Compares one RFC 8601 token with the token it is expected to be, which the grammar makes case-insensitive.</summary>
    private static bool Is(string? token, string expected) =>
        string.Equals(token?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
}
