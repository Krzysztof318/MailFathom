// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Governance;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>Configures how large a message this deployment is willing to compose and submit, and how it delivers one.</summary>
/// <remarks>
/// <para>
/// It is a section of its own rather than a block inside the synchronization settings, because it answers a question
/// about the whole deployment while the submission endpoint answers one about an account. Every account sends under the
/// same bounds — what a mailbox may send is a policy an operator holds once — and the endpoints they send through are
/// configured one at a time.
/// </para>
/// <para>
/// The delivery settings below are the same kind of decision: how much of one account's outbox a pass takes, how long
/// it holds it, and how patient this deployment is with a submission server that is not answering. None of them is a
/// per-account value either, because a provider that is briefly unreachable is answered the same way whichever mailbox
/// was waiting on it.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class MailDeliveryOptions : IValidatableObject
{
    /// <summary>The configuration section this deployment's sending bounds are read from.</summary>
    public const string SectionName = "MailDelivery";

    /// <summary>Gets or sets the greatest number of people one message may be addressed to.</summary>
    /// <remarks>
    /// The default is generous for correspondence and far below anything that reads as a mailing list, which this
    /// system refuses to be. The ceiling is what an outgoing record can hold, so a larger value would compose messages
    /// no record can be written for.
    /// </remarks>
    [Range(1, OutgoingEmailRequest.MaximumRecipientCount)]
    public int MaxRecipientCount { get; set; } = 50;

    /// <summary>Gets or sets the greatest number of characters either body of a message may carry.</summary>
    [Range(1, 10_000_000)]
    public int MaxBodyCharacters { get; set; } = 100_000;

    /// <summary>Gets or sets the greatest number of files one message may attach.</summary>
    [Range(0, 100)]
    public int MaxAttachmentCount { get; set; } = 10;

    /// <summary>Gets or sets the greatest number of octets one attached file may be made of.</summary>
    [Range(1, 100L * 1024 * 1024)]
    public long MaxAttachmentBytes { get; set; } = 10L * 1024 * 1024;

    /// <summary>Gets or sets the greatest number of octets the composed message may be transmitted as.</summary>
    /// <remarks>
    /// The default is the size most providers accept, and a deployment whose submission server accepts less is bounded
    /// by that server instead: what the server advertised is checked beside this number rather than in place of it.
    /// </remarks>
    [Range(1, 200L * 1024 * 1024)]
    public long MaxMessageBytes { get; set; } = 25L * 1024 * 1024;

    /// <summary>Gets or sets the greatest number of queued sends one delivery pass claims.</summary>
    /// <remarks>
    /// A send is a conversation with a submission server rather than a row to process, and a pass attempts them one at
    /// a time, so the useful values are small. What a pass leaves is claimed by the next one, oldest first.
    /// </remarks>
    [Range(1, 1000)]
    public int MaxDeliveriesPerPass { get; set; } = 10;

    /// <summary>Gets or sets how long a claim holds a queued send before another attempt may take it.</summary>
    /// <remarks>
    /// It is what makes a crash recoverable: a send in flight when a process stops is claimable again once this has
    /// passed, and nothing has to be told the process died. It never releases a send whose transmission had begun.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:30", "01:00:00")]
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Gets or sets how long one delivery attempt may run before it is cancelled.</summary>
    /// <remarks>
    /// It must stay below <see cref="LeaseDuration" />, and that ordering is the safety property rather than a
    /// preference: an attempt has to be cancelled before its lease can expire underneath it, because a lease that ran
    /// out while its holder was still transmitting is a second attempt taking a message the first may already have
    /// sent.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:10", "00:59:00")]
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromMinutes(7);

    /// <summary>Gets or sets how many attempts one send may be handed out for before it stops being attempted.</summary>
    /// <remarks>
    /// A send that spends them all stands where an operator can see it rather than being retried forever. A value of
    /// <c>1</c> leaves no retry at all, so the first failure that could have cleared is terminal.
    /// </remarks>
    [Range(1, 100)]
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Gets or sets the delay the first retry of a send is drawn around, from which the doubling grows.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Gets or sets the ceiling a grown retry delay never exceeds.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "24:00:00")]
    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Gets or sets who this deployment may write to.</summary>
    /// <remarks>
    /// A deployment that names nobody writes to anybody an enabled account is asked to write to, which is the posture
    /// an operator gets by writing nothing. Naming anybody at all narrows every account of the installation at once,
    /// because who an instance may correspond with is a decision about the instance.
    /// </remarks>
    public OutgoingRecipientPolicyOptions RecipientPolicy { get; set; } = new();

    /// <summary>Gets or sets how much mail one period may be asked to send.</summary>
    /// <remarks>
    /// It is the bound that turns a fault above it — a rule matching more mail than expected, a caller in a loop — into
    /// a refusal rather than into a provider suspending the account.
    /// </remarks>
    public OutgoingMailCeilingOptions SendCeilings { get; set; } = new();

    /// <summary>Gets or sets how many accounts may be waiting for a prompt delivery pass at once.</summary>
    /// <remarks>
    /// The queue holds accounts rather than messages, and an account already waiting is not queued twice, so it cannot
    /// grow past the number of configured accounts however much is enqueued. Raising it past that buys nothing;
    /// lowering it below that means a signal is occasionally refused, which delays those sends until the account's own
    /// synchronization run drains them rather than losing any.
    /// </remarks>
    [Range(1, 1000)]
    public int SignalQueueCapacity { get; set; } = 64;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // A message carries its attachments, their transfer encoding, and its own headers, so a per-file bound above
        // the whole-message bound describes a file that could never be sent — and the refusal an operator would meet is
        // the one about the message rather than the one they configured.
        if (this.MaxAttachmentCount > 0 && this.MaxAttachmentBytes > this.MaxMessageBytes)
        {
            yield return new ValidationResult(
                "MaxAttachmentBytes must not exceed MaxMessageBytes, because an attachment is transmitted inside the message that carries it.",
                [nameof(this.MaxAttachmentBytes)]);
        }

        // Refused rather than warned about: an attempt that outlives its lease is a second attempt taking a message the
        // first may already have transmitted, and the only thing standing between the two is this ordering.
        if (this.AttemptTimeout >= this.LeaseDuration)
        {
            yield return new ValidationResult(
                "AttemptTimeout must be shorter than LeaseDuration, so a delivery attempt is cancelled before its lease can expire and let a second attempt take the same message.",
                [nameof(this.AttemptTimeout)]);
        }

        if (this.RetryMaxDelay < this.RetryBaseDelay)
        {
            yield return new ValidationResult(
                "RetryMaxDelay must not be below RetryBaseDelay, because it is the ceiling the growing delay is capped at.",
                [nameof(this.RetryMaxDelay)]);
        }

        foreach (var result in this.RecipientPolicy.Validate())
        {
            yield return result;
        }

        foreach (var result in this.SendCeilings.Validate())
        {
            yield return result;
        }
    }
}

/// <summary>Configures the mailboxes and organizations this deployment may, and may never, write to.</summary>
/// <remarks>
/// <para>
/// Four flat lists rather than a list of entries, because an operator writing this down is answering one question per
/// list and never composing anything: a domain and an address are matched differently and are therefore written under
/// different keys, which is what keeps an entry from having to say which kind it is.
/// </para>
/// <para>
/// An entry that is not a domain or an address this system compares on fails startup naming its list. A policy is what
/// stands between a fault above and somebody's mailbox, so an entry that silently matched nothing would be a
/// restriction an operator believes they wrote and a permission this deployment actually holds.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class OutgoingRecipientPolicyOptions
{
    /// <summary>Gets or sets the organizations this deployment may write to, or nothing to admit every recipient no denied entry names.</summary>
    /// <remarks>An entry reaches the names beneath the domain as well as the domain itself, because an operator naming their own organization means its departments too.</remarks>
    public string[]? AllowedDomains { get; set; }

    /// <summary>Gets or sets the single mailboxes this deployment may write to, beside whatever the allowed domains admit.</summary>
    public string[]? AllowedAddresses { get; set; }

    /// <summary>Gets or sets the organizations this deployment may never write to, whatever the allowed lists say.</summary>
    public string[]? DeniedDomains { get; set; }

    /// <summary>Gets or sets the single mailboxes this deployment may never write to, whatever the allowed lists say.</summary>
    public string[]? DeniedAddresses { get; set; }

    /// <summary>Reads the four lists as the policy every outgoing message is judged against.</summary>
    /// <returns>The policy, which is the unrestricted one when no list names anybody.</returns>
    /// <remarks>An entry that does not parse is already a startup failure, so nothing here has to decide what an unusable one would have meant.</remarks>
    internal OutgoingRecipientPolicy ToPolicy() => OutgoingRecipientPolicy.Create(
        [.. ReadRules(this.AllowedDomains, OutgoingRecipientRule.TryCreateForDomain),
         .. ReadRules(this.AllowedAddresses, OutgoingRecipientRule.TryCreateForAddress)],
        [.. ReadRules(this.DeniedDomains, OutgoingRecipientRule.TryCreateForDomain),
         .. ReadRules(this.DeniedAddresses, OutgoingRecipientRule.TryCreateForAddress)]);

    /// <summary>Reports every entry that names no mailbox or organization this system compares on.</summary>
    /// <returns>One result per unusable entry, empty when every entry parses.</returns>
    /// <remarks>The entry itself is never quoted back: a recipient policy names people this deployment corresponds with, and a validation message reaches a startup log.</remarks>
    internal IEnumerable<ValidationResult> Validate()
    {
        foreach (var result in FindUnusableEntries(
            this.AllowedDomains,
            nameof(this.AllowedDomains),
            "domain",
            OutgoingRecipientRule.TryCreateForDomain))
        {
            yield return result;
        }

        foreach (var result in FindUnusableEntries(
            this.AllowedAddresses,
            nameof(this.AllowedAddresses),
            "address",
            OutgoingRecipientRule.TryCreateForAddress))
        {
            yield return result;
        }

        foreach (var result in FindUnusableEntries(
            this.DeniedDomains,
            nameof(this.DeniedDomains),
            "domain",
            OutgoingRecipientRule.TryCreateForDomain))
        {
            yield return result;
        }

        foreach (var result in FindUnusableEntries(
            this.DeniedAddresses,
            nameof(this.DeniedAddresses),
            "address",
            OutgoingRecipientRule.TryCreateForAddress))
        {
            yield return result;
        }
    }

    /// <summary>Reads one list into the entries it names, dropping nothing that parses.</summary>
    private static IEnumerable<OutgoingRecipientRule> ReadRules(
        string[]? entries,
        RuleParser parse)
    {
        foreach (var entry in entries ?? [])
        {
            if (parse(entry, out var rule))
            {
                yield return rule;
            }
        }
    }

    /// <summary>Reports the positions in one list whose entries name nothing this system compares on.</summary>
    private static IEnumerable<ValidationResult> FindUnusableEntries(
        string[]? entries,
        string listName,
        string describedKind,
        RuleParser parse)
    {
        foreach (var (entry, position) in (entries ?? []).Select((entry, position) => (entry, position)))
        {
            if (!parse(entry, out _))
            {
                yield return new ValidationResult(
                    $"MailDelivery:RecipientPolicy:{listName} entry {position} is not a {describedKind} this deployment can judge a recipient against.",
                    [$"{nameof(MailDeliveryOptions.RecipientPolicy)}.{listName}"]);
            }
        }
    }

    /// <summary>Reads one written entry as the rule it names, which is what both the validation and the policy do with it.</summary>
    private delegate bool RuleParser(string? entry, [NotNullWhen(true)] out OutgoingRecipientRule? rule);
}

/// <summary>Configures how much mail one period may be asked to send.</summary>
/// <remarks>
/// <para>
/// A ceiling of zero is no ceiling, which is what an operator gets by writing nothing. The period is a fixed window
/// anchored at the Unix epoch, so every process of a deployment agrees on where it begins with nothing stored to say
/// so, and a refused caller can be told when it rolls over.
/// </para>
/// <para>
/// Both pairs exist because they bound different faults. An account's ceiling is what one mailbox may be asked for, so
/// a rule gone wrong on one identity cannot spend the whole installation's allowance; the deployment's is the total,
/// which is the figure a provider would act on.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class OutgoingMailCeilingOptions
{
    /// <summary>The shortest window a ceiling is counted over, below which a period rolls over faster than a refusal is useful.</summary>
    private static readonly TimeSpan ShortestPeriod = TimeSpan.FromMinutes(1);

    /// <summary>The longest window a ceiling is counted over, above which an operator has described a quota rather than a bound.</summary>
    private static readonly TimeSpan LongestPeriod = TimeSpan.FromDays(31);

    /// <summary>Gets or sets the window every count is taken over.</summary>
    public TimeSpan Period { get; set; } = TimeSpan.FromDays(1);

    /// <summary>Gets or sets the messages one account may be asked for in a period, or zero for no ceiling.</summary>
    public long MaxMessagesPerAccount { get; set; }

    /// <summary>Gets or sets the recipients one account may be asked to write to in a period, or zero for no ceiling.</summary>
    public long MaxRecipientsPerAccount { get; set; }

    /// <summary>Gets or sets the messages this deployment may be asked for in a period, or zero for no ceiling.</summary>
    public long MaxMessagesPerDeployment { get; set; }

    /// <summary>Gets or sets the recipients this deployment may be asked to write to in a period, or zero for no ceiling.</summary>
    public long MaxRecipientsPerDeployment { get; set; }

    /// <summary>Reads the block as the ceilings every send is weighed against.</summary>
    /// <returns>The ceilings, which are the unbounded ones when the block declares none.</returns>
    internal OutgoingMailCeilings ToCeilings() => OutgoingMailCeilings.Create(
        this.Period,
        this.MaxMessagesPerAccount,
        this.MaxRecipientsPerAccount,
        this.MaxMessagesPerDeployment,
        this.MaxRecipientsPerDeployment);

    /// <summary>Reports a window this deployment does not count over, a negative ceiling, and a per-account ceiling above the deployment's own.</summary>
    /// <returns>One result per rule broken, empty when the block is one this deployment can apply.</returns>
    /// <remarks>
    /// <para>
    /// Written by hand rather than through the annotations the enclosing section uses, because the options validator
    /// judges a section's own properties and does not descend into a nested block — an annotation here would read as a
    /// bound and enforce nothing.
    /// </para>
    /// <para>
    /// A per-account ceiling above the deployment's own is refused rather than left to arithmetic, because it is a
    /// bound an operator wrote and this deployment would never apply: the refusal an account met would name the
    /// installation's ceiling rather than the one they configured for it.
    /// </para>
    /// </remarks>
    internal IEnumerable<ValidationResult> Validate()
    {
        if (this.Period < ShortestPeriod || this.Period > LongestPeriod)
        {
            yield return new ValidationResult(
                $"Period must be between {ShortestPeriod} and {LongestPeriod}, because it is the window every send is counted over.",
                [$"{nameof(MailDeliveryOptions.SendCeilings)}.{nameof(this.Period)}"]);
        }

        foreach (var (value, name) in new[]
        {
            (this.MaxMessagesPerAccount, nameof(this.MaxMessagesPerAccount)),
            (this.MaxRecipientsPerAccount, nameof(this.MaxRecipientsPerAccount)),
            (this.MaxMessagesPerDeployment, nameof(this.MaxMessagesPerDeployment)),
            (this.MaxRecipientsPerDeployment, nameof(this.MaxRecipientsPerDeployment)),
        })
        {
            if (value < 0)
            {
                yield return new ValidationResult(
                    $"{name} must not be negative; write zero for no ceiling at all.",
                    [$"{nameof(MailDeliveryOptions.SendCeilings)}.{name}"]);
            }
        }

        if (this.MaxMessagesPerAccount > 0
            && this.MaxMessagesPerDeployment > 0
            && this.MaxMessagesPerAccount > this.MaxMessagesPerDeployment)
        {
            yield return new ValidationResult(
                "MaxMessagesPerAccount must not exceed MaxMessagesPerDeployment, because the deployment's ceiling bounds every account of it.",
                [$"{nameof(MailDeliveryOptions.SendCeilings)}.{nameof(this.MaxMessagesPerAccount)}"]);
        }

        if (this.MaxRecipientsPerAccount > 0
            && this.MaxRecipientsPerDeployment > 0
            && this.MaxRecipientsPerAccount > this.MaxRecipientsPerDeployment)
        {
            yield return new ValidationResult(
                "MaxRecipientsPerAccount must not exceed MaxRecipientsPerDeployment, because the deployment's ceiling bounds every account of it.",
                [$"{nameof(MailDeliveryOptions.SendCeilings)}.{nameof(this.MaxRecipientsPerAccount)}"]);
        }
    }
}
