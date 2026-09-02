// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Frozen;
using MailFathom.Application.Folders;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Rules.Facts;

/// <summary>Resolves the declared facts for one email, computing each at most once and only when a condition names it.</summary>
/// <remarks>
/// <para>
/// This is where the cost bound the fact surface promises is actually kept. A condition reaches a fact by name, so a
/// fact no condition names is never asked for; a fact several conditions name is asked for once, because the answer is
/// remembered for the whole rule set rather than for one rule. Only <see cref="MailRuleFact.BodyText" /> costs anything
/// to resolve, and it is the fact those two properties exist for. <see cref="MailRuleFact.FolderRole" /> costs a look at
/// configuration instead of a read of stored content, which is cheap but is still not paid by a rule set that never
/// asks what a folder is for.
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
            [MailRuleFact.FolderRole] = facts => facts.ReadFolderRole(),
            [MailRuleFact.Subject] = facts => facts.email.Subject,
            [MailRuleFact.SenderAddress] = facts => facts.email.SenderAddress,
            [MailRuleFact.SenderDomain] = facts => facts.email.SenderDomain,
            [MailRuleFact.RecipientAddresses] = facts => facts.email.RecipientAddresses,
            [MailRuleFact.RecipientDomains] = facts => facts.email.RecipientDomains,
            [MailRuleFact.AuthorAuthentication] = facts => AuthoringName(facts.email.AuthorAuthentication),
            [MailRuleFact.SenderTrust] = facts => AuthoringName(facts.email.SenderTrust),
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
            [MailRuleFact.Keywords] = facts => facts.email.Keywords,
            [MailRuleFact.MachineAuthorship] = facts => AuthoringName(facts.email.MachineAuthorship),
            [MailRuleFact.HasExtractedContent] = facts => facts.email.HasExtractedContent,
        }.ToFrozenDictionary();

    private readonly MailRuleEmailFacts email;
    private readonly IMailRuleBodyTextReader bodyTextReader;
    private readonly IMailFolderMappingReader folderMappings;
    private readonly DateTimeOffset evaluatedAt;
    private readonly Dictionary<MailRuleFact, object?> resolvedValues = [];
    private readonly List<MailRuleFact> factsReadSinceLastTaken = [];

    /// <summary>Initializes the fact surface for one email at one instant.</summary>
    /// <param name="email">The metadata every fact but the body text and the folder's role is read from.</param>
    /// <param name="bodyTextReader">Reads the extracted body text, and is called only when a condition names it.</param>
    /// <param name="folderMappings">Answers what the folder the email is in is configured for, and is read only when a condition names the role.</param>
    /// <param name="evaluatedAt">The instant the whole rule set is evaluated at, which every age is measured against.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public MailRuleFacts(
        MailRuleEmailFacts email,
        IMailRuleBodyTextReader bodyTextReader,
        IMailFolderMappingReader folderMappings,
        DateTimeOffset evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(bodyTextReader);
        ArgumentNullException.ThrowIfNull(folderMappings);

        this.email = email;
        this.bodyTextReader = bodyTextReader;
        this.folderMappings = folderMappings;
        this.evaluatedAt = evaluatedAt;
    }

    /// <summary>Gets the facts that have actually been resolved, which is what proves an unnamed fact cost nothing.</summary>
    public IReadOnlyList<MailRuleFact> ResolvedFacts => [.. this.resolvedValues.Keys];

    /// <summary>Gets the configured identifier of the account this email belongs to.</summary>
    /// <remarks>
    /// Published beside the fact surface rather than reached through it, because deciding whether a rule applies to this
    /// account at all happens before the rule's condition is evaluated. Reading it that way also keeps it off
    /// <see cref="ResolvedFacts" />: a scope check is not a condition naming a fact, and counting it as one would make
    /// every pass look as though every rule had read the account.
    /// </remarks>
    public string Account => this.email.Account;

    /// <summary>Takes the facts read since this was last called, and begins recording afresh.</summary>
    /// <returns>The facts read, in the order they were first read.</returns>
    /// <remarks>
    /// Read rather than resolved, so a cache hit counts: what this answers is which facts a condition needed, and a
    /// condition that compares the sender's domain needed it whether or not the rule above it had already computed it.
    /// <see cref="ResolvedFacts" /> answers the other question — what this email cost — and the two must not be conflated.
    /// <para>
    /// Taking clears the record, because the caller is a rule set walking its rules one at a time and each rule's reads
    /// belong to that rule. It is called between rules rather than around one, which is what keeps this type free of any
    /// notion of which rule is running.
    /// </para>
    /// </remarks>
    public IReadOnlyList<MailRuleFact> TakeFactsRead()
    {
        if (this.factsReadSinceLastTaken.Count == 0)
        {
            return [];
        }

        var read = this.factsReadSinceLastTaken.ToArray();

        this.factsReadSinceLastTaken.Clear();

        return read;
    }

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

        this.Read(fact);

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

    /// <summary>Notes that the condition currently being evaluated reached this fact, once however often it reaches it.</summary>
    private void Read(MailRuleFact fact)
    {
        if (!this.factsReadSinceLastTaken.Contains(fact))
        {
            this.factsReadSinceLastTaken.Add(fact);
        }
    }

    /// <summary>Reads the role configuration gives the folder this email is in.</summary>
    /// <returns>The role's name, or <see langword="null" /> when the folder plays none or is no longer mapped.</returns>
    /// <remarks>
    /// The name is the one an operator writes, so a condition comparing against <c>'Junk'</c> reads the same word the
    /// folder's configuration and a rule's <c>role:Junk</c> destination do. A folder configuration has stopped naming
    /// answers absent rather than raising: a reload can withdraw a mapping while a pass over that account's mail is
    /// running, and a condition asking what the folder is for is honestly answered by nothing.
    /// </remarks>
    private string? ReadFolderRole() => this.folderMappings
        .FindFolderNamed(MailAccountId.Create(this.email.Account), MailFolderAlias.Create(this.email.Folder))
        ?.SpecialUse
        ?.ToString();

    /// <summary>Names a stored verdict the way a condition writes it.</summary>
    /// <remarks>
    /// The words are declared here rather than taken from the enumeration's own member names, so that renaming a
    /// member cannot silently change what an operator has to type — these are the authoring surface, and the MCP
    /// surface publishes its own copy of the same words for the same reason.
    /// </remarks>
    private static string AuthoringName(AuthorAuthenticationOutcome outcome) => outcome switch
    {
        AuthorAuthenticationOutcome.NotEstablished => "notEstablished",
        AuthorAuthenticationOutcome.Failed => "failed",
        AuthorAuthenticationOutcome.Authenticated => "authenticated",
        _ => throw new ArgumentOutOfRangeException(
            nameof(outcome),
            outcome,
            "The stored author-authentication outcome has no authoring name."),
    };

    /// <inheritdoc cref="AuthoringName(AuthorAuthenticationOutcome)" />
    private static string AuthoringName(SenderTrustLevel level) => level switch
    {
        SenderTrustLevel.Unknown => "unknown",
        SenderTrustLevel.Trusted => "trusted",
        _ => throw new ArgumentOutOfRangeException(
            nameof(level),
            level,
            "The stored sender-trust level has no authoring name."),
    };

    /// <inheritdoc cref="AuthoringName(AuthorAuthenticationOutcome)" />
    private static string AuthoringName(MachineAuthorshipBand band) => band switch
    {
        MachineAuthorshipBand.NotAssessed => "notAssessed",
        MachineAuthorshipBand.Unlikely => "unlikely",
        MachineAuthorshipBand.Possible => "possible",
        MachineAuthorshipBand.Likely => "likely",
        _ => throw new ArgumentOutOfRangeException(
            nameof(band),
            band,
            "The stored machine-authorship band has no authoring name."),
    };

    private static Func<MailRuleFacts, object?> ReadMetadata(MailRuleFact fact) =>
        MetadataReaders.TryGetValue(fact, out var reader)
            ? reader
            : throw new ArgumentException($"The fact '{fact}' has no resolution here.", nameof(fact));
}
