// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Mail.Mutations.Authoring;
using MailFathom.Application.Mail.Mutations.Authoring.Failures;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Mutations;
using MailFathom.Mcp.Tools.Results;
using ModelContextProtocol.Server;

namespace MailFathom.Mcp.Tools;

/// <summary>Publishes the <c>set_mail_flags</c> tool over the <see cref="MailFlagChangeRecorder" /> use case.</summary>
/// <param name="flagChangeRecorder">Writes the change down.</param>
/// <remarks>
/// <para>
/// It is the one tool on this surface that changes somebody's mailbox rather than MailFathom's copy of it, and it is
/// still not the thing that talks to a mail server: the use case behind it writes a durable record per value asked for,
/// and the account's own convergence pass issues the <c>STORE</c>. So a protocol request never waits on IMAP, never
/// opens a connection against an account's budget, and a crash between the record and the command leaves a change that
/// converges rather than a value that quietly disagrees with the mailbox.
/// </para>
/// <para>
/// The three values are one tool because they are one act. A caller triaging a message decides what to do with it once,
/// a server writes each of the three with the same command against the same UID, and three tools would make the common
/// case three round trips and three chances to have written half of it. What the tool does not do is invent a fourth
/// value: <c>\Answered</c> and <c>\Draft</c> each assert an act was performed, and
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md">ADR 0007</see>
/// refuses them for that reason.
/// </para>
/// <para>
/// It changes state, reaches outside this process eventually rather than during the call, and is not destructive: every
/// value it writes is reversible from any mail client with the gesture that would have made it, and reversible by
/// MailFathom itself since each direction exists. A replacement is the one call worth a second thought and its
/// annotation cannot say so — it is idempotent and reversible like the rest — so the description does, because a caller
/// working from a partial view of a message's labels can drop the ones it never saw.
/// </para>
/// <para>
/// Whether a caller may reach the tool at all is <see cref="McpToolAuthorization" />'s question, answered from
/// <see cref="RequiredPermission" /> in the listing as well as in the call. The use case asks for the same grant on its
/// own, so an entrypoint added later cannot write to a mailbox by arriving another way.
/// </para>
/// </remarks>
[McpServerToolType]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The MCP server materializes this tool type per tool call.")]
internal sealed class SetMailFlagsTool(MailFlagChangeRecorder flagChangeRecorder)
{
    /// <summary>The name the tool is advertised and called under.</summary>
    /// <remarks>Snake case because it is the naming the Model Context Protocol tool ecosystem uses; the C# member naming stops at the boundary.</remarks>
    public const string ToolName = "set_mail_flags";

    /// <summary>The capability a caller must hold to be offered this tool and to reach the use case behind it.</summary>
    /// <remarks>Changing somebody's mailbox is its own grant and does not follow from reading it, so a deployment that lets an agent read mail has not thereby let it star, unstar, or relabel any. Declaring it beside the name is what keeps <see cref="PublishedTools" /> able to answer for every tool this surface publishes.</remarks>
    public static MailFathomPermission RequiredPermission => MailFathomPermission.MailFlagsWrite;

    /// <summary>The greatest length text naming an email may carry before anything tries to read an identity out of it.</summary>
    /// <remarks>The bound and the reason are <c>get_email_content</c>'s: the longest form <see cref="Guid.TryParse(string, out Guid)" /> accepts is 68 characters, and the parse scans whatever it is handed.</remarks>
    private const int MaximumIdentifierLength = 68;

    /// <summary>The greatest length a caller-supplied request identity may carry.</summary>
    /// <remarks>It is the bound the durable record's own requester identity carries, checked here so a caller learns it named too long a value rather than meeting the domain's argument refusal.</remarks>
    private const int MaximumRequestIdLength = MailboxMutationRequester.MaximumIdentityLength;

    /// <summary>Writes the seen state, the flagged state, and the keywords a caller asks for onto one email.</summary>
    /// <param name="storedEmailId">The email to change, as a listing, a search, or a read returned it.</param>
    /// <param name="seen">Where to leave the <c>\Seen</c> flag, or absent to leave it alone.</param>
    /// <param name="flagged">Where to leave the <c>\Flagged</c> flag, or absent to leave it alone.</param>
    /// <param name="keywordChange">What to do with <paramref name="keywords" />, or absent to change no keyword.</param>
    /// <param name="keywords">The keywords the change names.</param>
    /// <param name="requestId">The caller's own identity for this request, which makes a retry the same request.</param>
    /// <param name="cancellationToken">Cancels the write when the caller disconnects or the host shuts down.</param>
    /// <returns>The durable record opened for each value asked for.</returns>
    /// <exception cref="StoredEmailIdentifierMalformedException">Thrown when the text names no email this system issued an identifier for.</exception>
    /// <exception cref="MailFlagChangeInvalidException">Thrown when the call asks for nothing, states half a keyword change, or names a keyword a mail server could not be asked to store.</exception>
    /// <exception cref="MailFathomException">
    /// Raised by the use case for a grant or an email it refuses. The call-tool filter turns every one of them into the
    /// coded result a client reads, so this tool neither catches nor re-describes any.
    /// </exception>
    [McpServerTool(
        Name = ToolName,
        Title = "Set mail flags",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description(
        "Marks one email read or unread, stars or unstars it, and adds, removes, or replaces its keywords — the labels "
        + "a mail client shows as tags. Every value is optional and at least one is required; a call that names none is "
        + "refused. The change is written down durably and issued to the mail server by the account's next "
        + "synchronization run, so the result reports the records rather than a mailbox that has already changed, and a "
        + "value that has not appeared in the owner's client after a few minutes is found by the changeRecordId this "
        + "returns. Every change is reversible: call again with the opposite value. keywordChange replace states the "
        + "whole keyword set — a keyword you do not list is removed, and an empty list clears them all — so read the "
        + "email's keywords first, or use add and remove, which touch only what they name. Only these three values can "
        + "be written: MailFathom never sets the answered or draft flags, never deletes mail, and never sends anything.")]
    public async Task<SetMailFlagsToolResult> SetMailFlagsAsync(
        [Description("The storedEmailId a listing, a search, or a read returned for the email. A UUID that does not change when the mail server renumbers or moves the message.")]
        string storedEmailId,
        [Description("true marks the email read, false marks it unread. Omit it to leave the flag where it stands. Reading mail through MailFathom never sets it, so this is the only way it moves from here.")]
        bool? seen = null,
        [Description("true stars the email, false unstars it. This is the flag a mail client draws as a star or a flag, and it is what the owner will see in their own client.")]
        bool? flagged = null,
        [Description("What to do with keywords: add puts the listed ones on beside whatever the email already carries, remove takes the listed ones off and leaves the rest, replace makes the keywords exactly the listed ones. Send it together with keywords; either one alone is refused.")]
        MailKeywordChangeDirection? keywordChange = null,
        [Description("The keywords the change names, at most 64, each at most 64 characters. A keyword is an IMAP atom: no space, no control character, none of ( ) { % * \" \\ ], nothing above plain ASCII, and no leading backslash, which is how system flags are spelled. Two spellings differing only in case are one keyword. An empty list is accepted only with replace, where it clears every keyword.")]
        IReadOnlyList<string>? keywords = null,
        [Description("Your own identifier for this request, at most 128 characters. Send the same one when retrying a call that may have gone through: the change is then the same request and is not made twice. A call with a new value, or with none, is a new request — which is what lets you star a message, unstar it, and star it again.")]
        string? requestId = null,
        CancellationToken cancellationToken = default)
    {
        var change = AuthoredMailFlagChange.Create(
            NamedEmail(storedEmailId),
            seen,
            flagged,
            keywordChange,
            keywords);

        var result = await flagChangeRecorder.RecordAsync(change, Requester(requestId), cancellationToken);

        return SetMailFlagsToolResult.From(result);
    }

    /// <summary>Reads the email identity the caller's text names.</summary>
    /// <remarks>
    /// The length is checked before the parse because a parse scans what it is handed, and the refused text is absent
    /// from the failure because it is the caller's own input on its way into a client-readable result. The empty UUID is
    /// refused with everything else, because it is what a client sends when it holds no identifier at all.
    /// </remarks>
    /// <exception cref="StoredEmailIdentifierMalformedException">Thrown when the text is not an identifier this system issues.</exception>
    private static StoredEmailId NamedEmail(string storedEmailId)
    {
        if (storedEmailId is null
            || storedEmailId.Length > MaximumIdentifierLength
            || !Guid.TryParse(storedEmailId, out var parsed)
            || parsed == Guid.Empty)
        {
            throw new StoredEmailIdentifierMalformedException();
        }

        return StoredEmailId.Create(parsed);
    }

    /// <summary>Names the invocation asking, from what the caller supplied or from an identity of MailFathom's own.</summary>
    /// <remarks>
    /// <para>
    /// A caller that sent nothing gets a fresh identity per call, which is the honest reading of a request that declined
    /// to say whether it was a retry: two such calls are two requests, and collapsing them would silently discard the
    /// second of a star and an unstar.
    /// </para>
    /// <para>
    /// The rules are checked here rather than left to the domain so a caller meets a refusal about the field it sent
    /// rather than an argument failure naming a parameter it never wrote. The domain checks them again where the
    /// record's column is bounded, which is where they stay enforced for every requester whatever boundary it arrived by.
    /// </para>
    /// </remarks>
    /// <exception cref="MailFlagChangeInvalidException">Thrown when the caller's identity is not one a record can be written under.</exception>
    private static MailboxMutationRequester Requester(string? requestId)
    {
        if (requestId is null)
        {
            return MailboxMutationRequester.Command(Guid.CreateVersion7().ToString());
        }

        if (string.IsNullOrWhiteSpace(requestId)
            || requestId.Length > MaximumRequestIdLength
            || requestId.Any(char.IsControl))
        {
            throw MailFlagChangeInvalidException.RequestIdNotUsable();
        }

        return MailboxMutationRequester.Command(requestId);
    }
}
