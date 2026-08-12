// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Rules.Facts;

/// <summary>Carries everything a condition can read about one email without a further read of stored content.</summary>
/// <remarks>
/// <para>
/// Every member here is metadata a caller already holds by the time a rule set is evaluated for an email, which is what
/// keeps the cost of a condition a property of the fact surface rather than of what somebody typed. The two derived
/// facts — the sender's domain and the recipients' domains — are computed from the addresses beside them rather than
/// carried separately, so nothing can supply an address and a domain that disagree.
/// </para>
/// <para>
/// The one fact this does not carry is the extracted body text, which reaches a condition through
/// <see cref="IMailRuleBodyTextReader" /> instead, and only when a condition names it.
/// </para>
/// </remarks>
public sealed record MailRuleEmailFacts
{
    /// <summary>Gets the configured alias of the account the email belongs to.</summary>
    public required string Account { get; init; }

    /// <summary>Gets the configured alias of the folder the email occurrence is in.</summary>
    public required string Folder { get; init; }

    /// <summary>Gets the subject line, or <see langword="null" /> when the email carries none.</summary>
    public string? Subject { get; init; }

    /// <summary>Gets the sender's address in its comparison form, or <see langword="null" /> when the email names no sender.</summary>
    public string? SenderAddress { get; init; }

    /// <summary>Gets the addresses the email was sent to and copied to, in their comparison form.</summary>
    public IReadOnlyList<string> RecipientAddresses { get; init; } = [];

    /// <summary>Gets when the last receiving hop recorded the email, or <see langword="null" /> when no hop recorded one.</summary>
    public DateTimeOffset? ReceivedAt { get; init; }

    /// <summary>Gets when the sender's client stamped the email, or <see langword="null" /> when it carries no such header.</summary>
    public DateTimeOffset? SentAt { get; init; }

    /// <summary>Gets the size of the whole email as the server reported it.</summary>
    public long SizeInBytes { get; init; }

    /// <summary>Gets how many attachments the email carries.</summary>
    public int AttachmentCount { get; init; }

    /// <summary>Gets the size of every attachment added together.</summary>
    public long AttachmentTotalBytes { get; init; }

    /// <summary>Gets whether the email's body is encrypted.</summary>
    public bool IsEncrypted { get; init; }

    /// <summary>Gets whether the email carries a signature part that nothing has verified.</summary>
    public bool CarriesUnverifiedSignature { get; init; }

    /// <summary>Gets whether the server reports the email as read.</summary>
    public bool IsSeen { get; init; }

    /// <summary>Gets whether the server reports the email as answered.</summary>
    public bool IsAnswered { get; init; }

    /// <summary>Gets whether the server reports the email as flagged.</summary>
    public bool IsFlagged { get; init; }

    /// <summary>Gets whether the server reports the email as a draft.</summary>
    public bool IsDraft { get; init; }

    /// <summary>Gets whether text has been extracted from the email's body.</summary>
    public bool HasExtractedContent { get; init; }

    /// <summary>Gets the part of the sender's address after the at sign, or <see langword="null" /> when there is no sender.</summary>
    public string? SenderDomain => ReadDomain(this.SenderAddress);

    /// <summary>Gets the distinct domains of every recipient address, in the order the addresses appear.</summary>
    public IReadOnlyList<string> RecipientDomains =>
    [
        .. this.RecipientAddresses
            .Select(ReadDomain)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase),
    ];

    /// <summary>Reads the domain out of an address, treating anything without a single trailing at sign as domainless.</summary>
    /// <remarks>
    /// The last at sign is the separator rather than the first, because a quoted local part may legitimately contain
    /// one. An address whose at sign is at either end names no domain, which is reported as absence rather than as an
    /// empty string a condition could match on by accident.
    /// </remarks>
    private static string? ReadDomain(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        var separator = address.LastIndexOf('@');

        return separator > 0 && separator < address.Length - 1
            ? address[(separator + 1)..]
            : null;
    }
}
