// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations.Authoring.Failures;

/// <summary>The failure raised when a request to write flags or keywords states no change a server could be asked for.</summary>
/// <remarks>
/// <para>
/// Every one of these is refused before anything is written down and before a mail server is involved at all, because a
/// request that cannot be carried must not become a durable record convergence will keep attempting. The refusals are
/// separate sentences rather than one, since each names a different thing for the caller to correct.
/// </para>
/// <para>
/// <strong>No keyword the caller wrote reaches this message.</strong> A label is text the owner or their client chose
/// and can name a person, a case, or a diagnosis, so an unusable one is reported as unusable and described by the rule
/// it broke. Every limit named here is MailFathom's own or IMAP's.
/// </para>
/// </remarks>
public sealed class MailFlagChangeInvalidException : MailFathomException
{
    private MailFlagChangeInvalidException(string operatorSafeMessage)
        : base(operatorSafeMessage)
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.MailFlagChangeInvalid;

    /// <summary>Refuses a request that named an email and asked for nothing to be done to it.</summary>
    /// <returns>The failure to raise.</returns>
    public static MailFlagChangeInvalidException NothingAsked() =>
        new("A request to write flags or keywords states at least one of the seen state, the flagged state, and a keyword change.");

    /// <summary>Refuses a request that stated one half of a keyword change and not the other.</summary>
    /// <returns>The failure to raise.</returns>
    public static MailFlagChangeInvalidException IncompleteKeywordChange() =>
        new("A keyword change states both what to do with the keywords and which keywords it means.");

    /// <summary>Refuses a keyword direction this system does not declare.</summary>
    /// <returns>The failure to raise.</returns>
    public static MailFlagChangeInvalidException UnknownKeywordDirection() =>
        new("A keyword change is an addition, a removal, or a replacement.");

    /// <summary>Refuses an addition or a removal that listed no keyword.</summary>
    /// <remarks>The replacement is not refused for it, because an empty replacement is how every keyword is cleared.</remarks>
    /// <returns>The failure to raise.</returns>
    public static MailFlagChangeInvalidException NoKeywordNamed() =>
        new("Adding or removing keywords names at least one keyword; only a replacement may name none, which clears them all.");

    /// <summary>Refuses a caller-supplied request identity a mutation record could not be written under.</summary>
    /// <returns>The failure to raise.</returns>
    /// <remarks>The identity is not repeated back. It is the caller's own text on its way into a client-readable result, and the rule it broke is what the caller can act on.</remarks>
    public static MailFlagChangeInvalidException RequestIdNotUsable() => new(
        string.Format(
            CultureInfo.InvariantCulture,
            "A request identity carries between one and {0} characters and no control character.",
            MailboxMutationRequester.MaximumIdentityLength));

    /// <summary>Refuses a request identity that already names this change on this email with different terms.</summary>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// The identity is what makes a retry the same request, so a second call sending it with a different value is
    /// either a caller reusing an identity it should have replaced or two callers that picked the same text. Answering
    /// with the earlier record would report the change as written down while the mailbox never moves, and the result
    /// publishes the record rather than the terms, so nothing the caller receives would say otherwise.
    /// </remarks>
    public static MailFlagChangeInvalidException RequestIdAlreadyAskedForAnother() =>
        new("A request identity already names this change on this email with different terms. Retry with the value the earlier call sent, or send a new request identity.");

    /// <summary>Refuses a keyword a mail server could not be asked to store.</summary>
    /// <returns>The failure to raise.</returns>
    public static MailFlagChangeInvalidException KeywordNotWritable() => new(
        string.Format(
            CultureInfo.InvariantCulture,
            "Every keyword is an IMAP atom of at most {0} characters that does not begin with a backslash, carries no space, control character, or any of ( ) {{ % * \" \\ ], and stays within US-ASCII; at most {1} may be named.",
            RemoteEmailKeywords.MaximumKeywordLength,
            RemoteEmailKeywords.MaximumKeywords));
}
