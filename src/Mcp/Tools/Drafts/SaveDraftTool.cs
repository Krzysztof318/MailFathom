// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Mcp.Tools.Results;
using ModelContextProtocol.Server;

namespace MailFathom.Mcp.Tools.Drafts;

/// <summary>Publishes the <c>save_draft</c> tool over the two use cases that write a draft.</summary>
/// <param name="drafts">Writes the draft the caller described, through the use case its shape names.</param>
/// <remarks>
/// <para>
/// It is the safe half of authoring mail, and the annotations are where that shows. Writing a draft is not read-only,
/// creates rather than takes away, and leaves a second draft when it is called twice — so three of the four values are
/// the opposite of <c>send_email</c>'s. <c>openWorldHint</c> is the one they share, and for a reason worth stating:
/// the draft is appended to the owner's own drafts folder on a mail server this deployment does not own, which is
/// <c>set_mail_flags</c>'s reach rather than a send's. Nothing this tool does reaches a third party.
/// </para>
/// <para>
/// <b>Nothing is sent.</b> A draft is offered to no submission server and to no recipient, and the only call that
/// changes that is <c>send_draft</c>, which requires the sending grant this tool does not. That separation is the whole
/// point of publishing four draft tools beside the sending ones: an agent can be given the half whose worst failure is
/// a message in somebody's Drafts folder.
/// </para>
/// <para>
/// One tool covers what the sending surface publishes as three, because a draft is not irreversible and does not need
/// three descriptions a client reads before deciding whether to ask a person. Which shape a call describes is read by
/// <see cref="DraftedMailWriting" />, and a call describing both or neither is refused rather than guessed at.
/// </para>
/// </remarks>
[McpServerToolType]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The MCP server materializes this tool type per tool call.")]
internal sealed class SaveDraftTool(DraftedMailWriting drafts)
{
    /// <summary>The name the tool is advertised and called under.</summary>
    /// <remarks>Snake case because it is the naming the Model Context Protocol tool ecosystem uses; the C# member naming stops at the boundary.</remarks>
    public const string ToolName = "save_draft";

    /// <summary>The capability a caller must hold to be offered this tool and to reach the use cases behind it.</summary>
    /// <remarks>It is the drafting grant rather than the sending one, which is what lets a deployment give an agent the ability to prepare mail without the ability to send any. A caller holding it and nothing else is offered this tool, <c>update_draft</c>, and <c>delete_draft</c>, and is not offered <c>send_draft</c>.</remarks>
    public static MailFathomPermission RequiredPermission => MailFathomPermission.MailDraftsWrite;

    /// <summary>Writes one message into the drafts folder of an account this deployment holds.</summary>
    /// <param name="plainTextBody">The plain-text body every draft carries.</param>
    /// <param name="account">The account the draft belongs to, or absent where the draft answers a stored email.</param>
    /// <param name="subject">The subject line, or absent where the draft answers a stored email.</param>
    /// <param name="to">The addresses the draft is addressed to, or absent to address nobody yet.</param>
    /// <param name="cc">The addresses to copy, or absent to copy nobody.</param>
    /// <param name="bcc">The addresses to copy without naming them to anybody else, or absent to blind-copy nobody.</param>
    /// <param name="htmlBody">The HTML alternative, or absent to write the plain text alone.</param>
    /// <param name="answeredEmailId">The stored email the draft answers, or absent where it answers none.</param>
    /// <param name="answering">Which answer the draft is, or absent where it answers no stored email.</param>
    /// <param name="cancellationToken">Cancels the write when the caller disconnects or the host shuts down.</param>
    /// <returns>The draft this deployment now holds.</returns>
    /// <exception cref="MailDraftRefusedException">Thrown when the arguments describe no draft this system writes, a recipient carries no address, or the account is not one a name could be read out of.</exception>
    /// <exception cref="MailFathomException">
    /// Raised by the use cases for a grant, an account, an answered email, a recipient, a field, or a bound they refuse.
    /// The call-tool filter turns every one of them into the coded result a client reads, so this tool neither catches
    /// nor re-describes any.
    /// </exception>
    [McpServerTool(
        Name = ToolName,
        Title = "Save draft",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description(
        "Writes a message into the owner's own Drafts folder and SENDS NOTHING. Nobody receives it, no mail server is "
        + "offered it, and the only person who ever sees it is the mailbox's owner, in their own mail client. Use it "
        + "when the person you are acting for should read a message before it goes out; sending it afterwards is "
        + "send_draft, a separate tool behind a separate permission that this tool does not grant. A draft can be "
        + "edited with update_draft and taken back with delete_draft, so nothing here is final. "
        + "There are two shapes and a call states exactly one of them. A message of its own: name account and subject, "
        + "and address it with to, cc, and bcc. An answer to mail this deployment already holds: name answeredEmailId "
        + "and answering, and name NEITHER account NOR subject — the account, the subject, the threading headers that "
        + "put the answer in the right conversation, the quoted original, and the files a forward carries are all read "
        + "from the stored email, and to, cc, and bcc then add people beside whoever the answer already reaches. A "
        + "call that states both shapes, or neither, is refused rather than guessed at. "
        + "A draft addressed to nobody is an ordinary draft — writing the message before deciding who reads it is what "
        + "drafting is for — and send_draft is what refuses it later, so nothing here requires a recipient. "
        + "Calling this twice writes TWO drafts: there is no idempotency key, because a duplicate costs a deletion "
        + "rather than a recipient a second message, so a retry after a timeout leaves a second draft to remove with "
        + "delete_draft rather than one message sent twice. To change a draft, call update_draft with the draftId this "
        + "answers rather than saving again. "
        + "The From address is not an argument — the draft belongs to the account you name, or to the account the "
        + "answered email is in, and that account's configuration decides the address. This tool will not attach "
        + "files, will not schedule anything, and will not send.")]
    public async Task<SaveDraftToolResult> SaveDraftAsync(
        [Description("The message body as plain text, which every draft carries. It is required even when you also write htmlBody: a plain text derived by stripping markup reads as damage in the clients that show it, so the text you write here is what is stored. On an answer it is placed above the quoted original, which is added for you — do not paste or paraphrase the message being answered.")]
        string plainTextBody,
        [Description("The account the draft belongs to, named by the accountId or the display name list_accounts returned. Required for a message of its own, and refused on an answer, where the account is read from the stored email being answered.")]
        string? account = null,
        [Description("The subject line, as it will be stored. Required for a message of its own — empty text is allowed and means a message nobody has titled yet — and refused on an answer, where the subject is derived from the email being answered. A line break in it is refused, because a subject is written into a header.")]
        string? subject = null,
        [Description("The addresses the draft is addressed to, one entry per person, each a plain mail address such as person@example.com without a display name. Omit it to address nobody yet, which is an ordinary draft. On a reply these are added beside the people the answer already reaches rather than replacing them; on a forward they are what the message would go to.")]
        IReadOnlyList<string>? to = null,
        [Description("The addresses to copy, each a plain mail address. Everybody the message would reach can see them. Omit it to copy nobody.")]
        IReadOnlyList<string>? cc = null,
        [Description("The addresses to copy without naming them to anybody else. No other recipient would see that they received it. Omit it to blind-copy nobody.")]
        IReadOnlyList<string>? bcc = null,
        [Description("An HTML alternative to plainTextBody, stored beside it so each client shows the one it prefers. Omit it to write the plain text alone. It is the same message written twice, not a second message.")]
        string? htmlBody = null,
        [Description("The storedEmailId a listing, a search, a read, or an answer returned for the email this draft answers. Name it together with answering, and name neither to draft a message of its own.")]
        string? answeredEmailId = null,
        [Description("Which answer this draft is, required whenever answeredEmailId is named and refused otherwise. The three reach three different sets of people, so state it deliberately.")]
        DraftedAnswer? answering = null,
        CancellationToken cancellationToken = default)
    {
        var fields = new DraftedMailFields
        {
            Account = account,
            Subject = subject,
            PlainTextBody = plainTextBody,
            HtmlBody = htmlBody,
            To = to,
            Cc = cc,
            Bcc = bcc,
            AnsweredEmailId = answeredEmailId,
            Answers = answering,
        };

        var draft = await drafts.SaveAsync(fields, revises: null, cancellationToken);

        return SaveDraftToolResult.From(draft);
    }
}
