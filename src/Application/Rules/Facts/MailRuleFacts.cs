// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Frozen;

namespace MailFathom.Application.Rules.Facts;

/// <summary>Resolves the declared facts for one email, computing each at most once and only when a condition names it.</summary>
/// <remarks>
/// <para>
/// This is where the cost bound the fact surface promises is actually kept. A condition reaches a fact by name, so a
/// fact no condition names is never asked for; a fact several conditions name is asked for once, because the answer is
/// remembered for the whole rule set rather than for one rule. Only <see cref="MailRuleFact.BodyText" /> costs anything
/// to resolve, and it is the fact those two properties exist for.
/// </para>
/// <para>
/// The evaluation instant is taken once, when this is constructed, rather than read per rule. Otherwise
/// <see cref="MailRuleFact.AgeInDays" /> could answer differently to two rules of the same pass, and a rule set would
/// stop being one reading of one email.
/// </para>
/// <para>
/// One instance serves one evaluation of one rule set and is not safe to use from several at once. Nothing here locks,
/// because an evaluation walks its rules in declared order and a condition's own operands are evaluated one at a time.
/// </para>
/// </remarks>
public sealed class MailRuleFacts
{
    /// <summary>Reads every fact that comes from already-loaded metadata, as a table rather than as a chain of branches.</summary>
    /// <remarks>
    /// Text arrives as a string, a text set as a list of strings, a number as a <see cref="double" />, a boolean as
    /// itself, and a timestamp as a UTC <see cref="DateTime" />. The timestamp conversion is what lets a condition
    /// compare a fact against a date literal, which the expression language parses as a <see cref="DateTime" /> carrying
    /// no offset of its own. Every number is widened to the same type so that a comparison against a literal never
    /// depends on which numeric type a fact happened to be stored in.
    /// </remarks>
    private static readonly FrozenDictionary<MailRuleFact, Func<MailRuleFacts, object?>> MetadataReaders =
        new Dictionary<MailRuleFact, Func<MailRuleFacts, object?>>
        {
            [MailRuleFact.Account] = facts => facts.email.Account,
            [MailRuleFact.Folder] = facts => facts.email.Folder,
            [MailRuleFact.Subject] = facts => facts.email.Subject,
            [MailRuleFact.SenderAddress] = facts => facts.email.SenderAddress,
            [MailRuleFact.SenderDomain] = facts => facts.email.SenderDomain,
            [MailRuleFact.RecipientAddresses] = facts => facts.email.RecipientAddresses,
            [MailRuleFact.RecipientDomains] = facts => facts.email.RecipientDomains,
            [MailRuleFact.ReceivedAt] = facts => facts.email.ReceivedAt?.UtcDateTime,
            [MailRuleFact.SentAt] = facts => facts.email.SentAt?.UtcDateTime,
            [MailRuleFact.AgeInDays] = facts => facts.email.ReceivedAt is { } receivedAt
                ? (facts.evaluatedAt - receivedAt).TotalDays
                : null,
            [MailRuleFact.SizeInBytes] = facts => (double)facts.email.SizeInBytes,
            [MailRuleFact.AttachmentCount] = facts => (double)facts.email.AttachmentCount,
            [MailRuleFact.AttachmentTotalBytes] = facts => (double)facts.email.AttachmentTotalBytes,
            [MailRuleFact.IsEncrypted] = facts => facts.email.IsEncrypted,
            [MailRuleFact.CarriesUnverifiedSignature] = facts => facts.email.CarriesUnverifiedSignature,
            [MailRuleFact.IsSeen] = facts => facts.email.IsSeen,
            [MailRuleFact.IsAnswered] = facts => facts.email.IsAnswered,
            [MailRuleFact.IsFlagged] = facts => facts.email.IsFlagged,
            [MailRuleFact.IsDraft] = facts => facts.email.IsDraft,
            [MailRuleFact.HasExtractedContent] = facts => facts.email.HasExtractedContent,
        }.ToFrozenDictionary();

    private readonly MailRuleEmailFacts email;
    private readonly IMailRuleBodyTextReader bodyTextReader;
    private readonly DateTimeOffset evaluatedAt;
    private readonly Dictionary<MailRuleFact, object?> resolvedValues = [];

    /// <summary>Initializes the fact surface for one email at one instant.</summary>
    /// <param name="email">The metadata every fact but the body text is read from.</param>
    /// <param name="bodyTextReader">Reads the extracted body text, and is called only when a condition names it.</param>
    /// <param name="evaluatedAt">The instant the whole rule set is evaluated at, which every age is measured against.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public MailRuleFacts(MailRuleEmailFacts email, IMailRuleBodyTextReader bodyTextReader, DateTimeOffset evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(bodyTextReader);

        this.email = email;
        this.bodyTextReader = bodyTextReader;
        this.evaluatedAt = evaluatedAt;
    }

    /// <summary>Gets the facts that have actually been resolved, which is what proves an unnamed fact cost nothing.</summary>
    public IReadOnlyList<MailRuleFact> ResolvedFacts => [.. this.resolvedValues.Keys];

    /// <summary>Resolves one fact's value in the form a condition compares against.</summary>
    /// <param name="fact">The declared fact a condition named.</param>
    /// <param name="cancellationToken">Cancels a resolution that reads stored content.</param>
    /// <returns>The value, which is <see langword="null" /> for a fact this email carries nothing for.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="fact" /> is the unspecified struct default.</exception>
    public async Task<object?> ResolveAsync(MailRuleFact fact, CancellationToken cancellationToken)
    {
        if (!fact.IsSpecified)
        {
            throw new ArgumentException("The unspecified default of the struct does not name a fact.", nameof(fact));
        }

        if (this.resolvedValues.TryGetValue(fact, out var alreadyResolved))
        {
            return alreadyResolved;
        }

        var value = fact == MailRuleFact.BodyText
            ? await this.bodyTextReader.ReadBodyTextAsync(cancellationToken)
            : ReadMetadata(fact)(this);

        this.resolvedValues[fact] = value;

        return value;
    }

    private static Func<MailRuleFacts, object?> ReadMetadata(MailRuleFact fact) =>
        MetadataReaders.TryGetValue(fact, out var reader)
            ? reader
            : throw new ArgumentException($"The fact '{fact}' has no resolution here.", nameof(fact));
}
