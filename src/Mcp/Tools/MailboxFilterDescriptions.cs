// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Mcp.Tools;

/// <summary>The published wording of the filters <c>list_emails</c> and <c>search_emails</c> narrow by identically.</summary>
/// <remarks>
/// <para>
/// A filter both tools apply to the same stored column, in the same way, with the same refusals, is one statement to a
/// client rather than two: a caller that learned what <c>keyword</c> means from a listing must not have to read it
/// again to find out whether a search means something else by it. Written twice, the two answers drift by an edit to
/// one of them, and the drift is invisible — the descriptions are published prose that no build compares.
/// </para>
/// <para>
/// Only the filters that are genuinely the same statement are here. <c>subjectFragment</c> and <c>isRemotelySeen</c>
/// are not: the first has to say that it narrows what is eligible rather than what the ranking matches, which is a
/// sentence a listing has nothing to say, and the second names the act that does not change the flag, which is a
/// different act in each tool. <c>accounts</c>, <c>folders</c>, and <c>includeJunkMail</c> are not either, for the
/// same reason — each says <em>read</em> or <em>search</em> about itself.
/// </para>
/// <para>
/// The tools' signatures are untouched by this: each still declares every parameter it takes, and the published input
/// schema is the same schema, because a <c>const</c> reference in an attribute is the literal it names.
/// </para>
/// </remarks>
internal static class MailboxFilterDescriptions
{
    /// <summary>The <c>senderAddress</c> filter, matched whole rather than as a fragment.</summary>
    public const string SenderAddress =
        "Return only emails sent from this mail address. Matched as a whole address rather than as a fragment, "
        + "without regard to case; a non-empty value that is not a usable mail address is refused. Omit to match any "
        + "sender, which an empty string does too.";

    /// <summary>The <c>recipientAddress</c> filter, which reads <c>To</c> and <c>Cc</c> and never <c>Reply-To</c>.</summary>
    public const string RecipientAddress =
        "Return only emails addressed to this mail address in their To or Cc header. Matched as a whole address rather "
        + "than as a fragment; Reply-To is not searched. Omit to match any recipient, which an empty string does too.";

    /// <summary>The inclusive lower bound of the received range.</summary>
    public const string ReceivedOnOrAfter =
        "Return only emails received at or after this ISO 8601 timestamp. Emails whose received date is unknown are "
        + "excluded whenever either bound is named. Omit for no lower bound.";

    /// <summary>The exclusive upper bound of the received range, which is what makes consecutive ranges meet exactly.</summary>
    public const string ReceivedBefore =
        "Return only emails received strictly before this ISO 8601 timestamp, so consecutive ranges built from one "
        + "instant neither overlap nor leave a gap. Omit for no upper bound.";

    /// <summary>The <c>isRemotelyFlagged</c> filter, which is the star a mail client draws rather than the folder role.</summary>
    public const string IsRemotelyFlagged =
        "Return only emails the mail server last reported as flagged (true) or unflagged (false), which is the star "
        + "most mail clients show. Omit to match either. This is the \\Flagged flag on a message and is unrelated to "
        + "the Flagged folder role; an email whose flags no run has observed yet counts as unflagged.";

    /// <summary>The <c>keyword</c> filter, matched as a whole keyword.</summary>
    public const string Keyword =
        "Return only emails carrying this keyword, which is a flag a mail client or server set rather than one of the "
        + "five standard ones, such as $Junk or a label. Matched as a whole keyword without regard to case; up to 64 "
        + "characters, and a value that is not a keyword this system stores is refused. Omit to match any, which an "
        + "empty string does too. The keywords each email carries are reported in its remoteFlags.";

    /// <summary>The <c>hasAttachments</c> filter, which counts neither inline images nor signature parts.</summary>
    public const string HasAttachments =
        "Return only emails that carry attachments (true) or that carry none (false). Omit to match either. Inline "
        + "images and cryptographic signature parts do not count as attachments.";
}
