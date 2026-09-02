// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Authoring;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace MailFathom.Host.Api;

/// <summary>Serves the messages the signed-in owner is writing: composing one, attaching to one, and sending it.</summary>
/// <remarks>
/// <para>
/// A draft here is the one kind there is: every save files it in the owner's own drafts folder, so what these routes
/// write is what their mail client shows them and what their phone syncs. There is no local-only draft to reach
/// instead, deliberately — a message held where its owner never looks is a second place to look for the same thing.
/// What it costs is an <c>APPEND</c> and a removal per revision, IMAP having no command that changes a stored message,
/// which is why a revision is a request the client makes when somebody asks to save rather than one it makes as they
/// type.
/// </para>
/// <para>
/// <b>Every route is scoped to the caller's own owner</b>, and a draft another owner holds answers exactly as one
/// nobody holds. A save names the account it belongs to and that name is resolved against the accounts the caller's
/// owner owns; every other act names a draft, and <see cref="OwnerMailDrafts" /> and <see cref="MailDraftDirectory" />
/// are where an identifier becomes a draft this owner holds before anything acts on it.
/// </para>
/// <para>
/// The grants are two rather than one, because writing a draft and sending it are different powers. Writing, listing,
/// opening, revising, giving up, and attaching are <c>mailfathom.mail.drafts.write</c>, whose effect reaches the
/// owner's own mailbox and nobody else's; sending is <c>mailfathom.mail.send</c>, which puts a message in somebody
/// else's mailbox and is the one act here that cannot be taken back. They are the same two names the drafting tools
/// are published under, because a draft written here and one written by an agent are the same row and the same copy.
/// </para>
/// <para>
/// A refused send says which of this deployment's rules refused it and what would change the outcome, and echoes none
/// of what triggered the refusal: the codes and the sentences are the failures' own, written for an operator to read
/// and carrying no recipient, no subject, and no fragment of the message. Nothing on any of these routes reaches a
/// log, a span attribute, or a telemetry event either.
/// </para>
/// </remarks>
internal static class ClientDraftEndpoints
{
    /// <summary>The route the owner's drafts are listed at and a new one is written at, relative to the client prefix.</summary>
    internal const string DraftsRoute = "/drafts";

    /// <summary>The route one draft is opened, revised, and given up at.</summary>
    internal const string DraftRoute = $"{DraftsRoute}/{{draftId:guid}}";

    /// <summary>The route one draft is sent at.</summary>
    internal const string DraftSendRoute = $"{DraftRoute}/send";

    /// <summary>The route one file is staged against a draft at.</summary>
    internal const string DraftAttachmentsRoute = $"{DraftRoute}/attachments";

    /// <summary>The route one staged file is taken back off a draft at.</summary>
    internal const string DraftAttachmentRoute = $"{DraftAttachmentsRoute}/{{attachmentId:guid}}";

    /// <summary>The <c>answers</c> value naming a reply to the author of the message alone.</summary>
    internal const string SenderOnlyAnswer = "senderOnly";

    /// <summary>The <c>answers</c> value naming a reply to everybody the message reached.</summary>
    internal const string EveryoneAnswer = "everyone";

    /// <summary>The <c>answers</c> value naming a forward.</summary>
    internal const string ForwardAnswer = "forward";

    /// <summary>The greatest request body a write to a draft reads before refusing it.</summary>
    /// <remarks>
    /// A draft carries the text somebody typed and the addresses they named, and the composition refuses a body longer
    /// than the deployment composes anyway. Stated for the reason every other write on this surface states one: the
    /// server's own default is measured in tens of megabytes, which here would let an authenticated client make the
    /// process buffer a body orders of magnitude larger than any draft it could save. The files are not in it — an
    /// upload is a route of its own bounded by the operator's own attachment size.
    /// </remarks>
    internal const int MaxWriteRequestBytes = 2 * 1024 * 1024;

    /// <summary>Maps the draft routes into the client group, so they inherit its requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The upload's bound is the operator's configured attachment size rather than a constant, read here from the
    /// options the deployment bound at startup. A transport bound smaller than the one the use case enforces would
    /// refuse a file the deployment says it composes, and a larger one would let the process buffer octets no draft
    /// could ever carry — so there is one number and this is where the routing pipeline is told it.
    /// </remarks>
    internal static void MapClientDrafts(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var uploadLimit = ((IEndpointRouteBuilder)api).ServiceProvider
            .GetRequiredService<IOptions<MailDeliveryOptions>>()
            .Value
            .ToOutgoingEmailBounds()
            .MaxAttachmentBytes;

        api.MapGet(DraftsRoute, ReadDraftsAsync)
            .RequirePermission(MailFathomPermission.MailDraftsWrite);

        api.MapGet(DraftRoute, ReadDraftAsync)
            .RequirePermission(MailFathomPermission.MailDraftsWrite);

        // The attribute is reached for its metadata rather than as an MVC filter, exactly as every other write on this
        // surface reaches it: it implements IRequestSizeLimitMetadata, which the routing pipeline applies to the
        // request body feature, so a body over the bound is answered 413 before the handler is reached.
        api.MapPost(DraftsRoute, WriteDraftAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailDraftsWrite);

        api.MapPut(DraftRoute, ReviseDraftAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailDraftsWrite);

        api.MapDelete(DraftRoute, DiscardDraftAsync)
            .RequirePermission(MailFathomPermission.MailDraftsWrite);

        api.MapPost(DraftSendRoute, SendDraftAsync)
            .RequirePermission(MailFathomPermission.MailSend);

        api.MapPost(DraftAttachmentsRoute, StageAttachmentAsync)
            .WithMetadata(new RequestSizeLimitAttribute(uploadLimit))
            .Accepts<Stream>(AttachmentContentResponse.FallbackMediaType)
            .RequirePermission(MailFathomPermission.MailDraftsWrite);

        api.MapDelete(DraftAttachmentRoute, UnstageAttachmentAsync)
            .RequirePermission(MailFathomPermission.MailDraftsWrite);
    }

    /// <summary>Serves the drafts the acting owner is writing, newest edit first.</summary>
    /// <param name="account">The account to narrow to, by its identifier or its display name, or <see langword="null" /> for every account the owner owns.</param>
    /// <param name="directory">Reads the drafts, for a caller the read's own grant admits.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the drafts, <c>400</c> naming what was wrong with the request, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.drafts.write</c>.</returns>
    internal static async Task<Results<Ok<ClientDraftListResponse>, ProblemHttpResult>> ReadDraftsAsync(
        [FromQuery] string? account,
        [FromServices] MailDraftDirectory directory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(directory);

        if (!TryReadAccount(account, out var named))
        {
            return Refuse("The account names a value this deployment does not issue.");
        }

        try
        {
            return TypedResults.Ok(
                ClientDraftListResponse.For(await directory.ReadAsync(named, cancellationToken)));
        }
        catch (MailAccountNotAccessibleException)
        {
            return Refuse("The account is not one this owner owns.");
        }
    }

    /// <summary>Opens one of the acting owner's drafts, with the words its stored message carries.</summary>
    /// <param name="draftId">The draft to open.</param>
    /// <param name="directory">Reads the draft and its message, for a caller the read's own grant admits.</param>
    /// <param name="cancellationToken">Cancels the reads when the client disconnects.</param>
    /// <returns><c>200</c> with the draft and its text, or <c>404</c> where this owner holds no such draft.</returns>
    /// <remarks>
    /// A draft another owner holds, one nobody holds, and one whose stored message has gone answer identically, so
    /// nothing here tells a caller that somebody else's draft exists.
    /// </remarks>
    internal static async Task<Results<Ok<ClientDraftReadingResponse>, NotFound>> ReadDraftAsync(
        [FromRoute] Guid draftId,
        [FromServices] MailDraftDirectory directory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(directory);

        if (HeldDraft(draftId) is not { } identifier)
        {
            return TypedResults.NotFound();
        }

        return await directory.ReadComposedAsync(identifier, cancellationToken) is { } reading
            ? TypedResults.Ok(ClientDraftReadingResponse.For(reading))
            : TypedResults.NotFound();
    }

    /// <summary>Writes one new draft for the acting owner, which reaches no mailbox unless the request asks it to.</summary>
    /// <param name="request">What the author wrote.</param>
    /// <param name="drafting">Writes a draft of a message of its own.</param>
    /// <param name="answering">Writes a draft of a reply, a reply to all, or a forward.</param>
    /// <param name="cancellationToken">Cancels the reads and the writes.</param>
    /// <returns><c>200</c> with the draft, or <c>400</c> naming what the author has to change.</returns>
    internal static Task<Results<Ok<ClientDraftResponse>, ProblemHttpResult>> WriteDraftAsync(
        [FromBody] ClientDraftWriteRequest? request,
        [FromServices] AuthoredMailDrafting drafting,
        [FromServices] AuthoredResponseDrafting answering,
        CancellationToken cancellationToken) =>
        SaveAsync(request, revises: null, drafting, answering, cancellationToken);

    /// <summary>Replaces one of the acting owner's drafts with what the author has written since.</summary>
    /// <param name="draftId">The draft being replaced.</param>
    /// <param name="request">What the author wrote.</param>
    /// <param name="drafting">Writes a draft of a message of its own.</param>
    /// <param name="answering">Writes a draft of a reply, a reply to all, or a forward.</param>
    /// <param name="cancellationToken">Cancels the reads and the writes.</param>
    /// <returns><c>200</c> with the draft, <c>404</c> where this owner holds no such draft, or <c>400</c> naming what the author has to change.</returns>
    /// <remarks>
    /// The revision keeps whichever shape the draft already is: an answer re-derives its account, its subject, and its
    /// threading identifiers from the message it answers rather than from the revision it replaces, which is what keeps
    /// an edited reply a reply. Whether a copy belongs in the owner's folder is not read here — that was settled when
    /// the draft was written, and asking it again per edit would let a message be filed by an edit about a subject.
    /// </remarks>
    internal static Task<Results<Ok<ClientDraftResponse>, ProblemHttpResult>> ReviseDraftAsync(
        [FromRoute] Guid draftId,
        [FromBody] ClientDraftWriteRequest? request,
        [FromServices] AuthoredMailDrafting drafting,
        [FromServices] AuthoredResponseDrafting answering,
        CancellationToken cancellationToken) =>
        HeldDraft(draftId) is { } identifier
            ? SaveAsync(request, identifier, drafting, answering, cancellationToken)
            : Task.FromResult<Results<Ok<ClientDraftResponse>, ProblemHttpResult>>(NoDraftNamed());

    /// <summary>Gives up one of the acting owner's drafts, and takes its copies back out of their folder.</summary>
    /// <param name="draftId">The draft to give up.</param>
    /// <param name="drafts">Performs the act, for the owner the credential names.</param>
    /// <param name="cancellationToken">Cancels the reads and the writes.</param>
    /// <returns><c>200</c> with what became of the copies, <c>404</c> where this owner holds no such draft, or <c>409</c> where the draft has already been sent.</returns>
    /// <remarks>
    /// A draft already promoted to a send is refused rather than given up, because its message is a queued send this
    /// would leave running: cancelling the send in the outbox is what stops it, and until it is delivered or cancelled
    /// the draft stands.
    /// </remarks>
    internal static async Task<Results<Ok<ClientDraftDiscardResponse>, ProblemHttpResult>> DiscardDraftAsync(
        [FromRoute] Guid draftId,
        [FromServices] OwnerMailDrafts drafts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(drafts);

        if (HeldDraft(draftId) is not { } identifier)
        {
            return NoDraftNamed();
        }

        try
        {
            return TypedResults.Ok(
                ClientDraftDiscardResponse.For(await drafts.DiscardAsync(identifier, cancellationToken)));
        }
        catch (MailDraftRefusedException refusal)
        {
            return Refused(refusal);
        }
    }

    /// <summary>Queues one of the acting owner's drafts for delivery, which is the one act here that reaches anybody else.</summary>
    /// <param name="draftId">The draft to send.</param>
    /// <param name="drafts">Performs the act, for the owner the credential names.</param>
    /// <param name="cancellationToken">Cancels the reads and the write.</param>
    /// <returns><c>200</c> with the queued send, <c>404</c> where this owner holds no such draft, <c>409</c> where a rule of this deployment refused the message, or <c>503</c> where screening could not run.</returns>
    /// <remarks>
    /// Nothing has been transmitted when this answers: the message is queued and the outbox routes are where a client
    /// watches what becomes of it. A refusal names which rule refused — screening, the recipient policy, or a spending
    /// ceiling — through the code beside the sentence, and echoes nothing of what triggered it.
    /// </remarks>
    internal static async Task<Results<Ok<ClientDraftSendResponse>, ProblemHttpResult>> SendDraftAsync(
        [FromRoute] Guid draftId,
        [FromServices] OwnerMailDrafts drafts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(drafts);

        if (HeldDraft(draftId) is not { } identifier)
        {
            return NoDraftNamed();
        }

        try
        {
            var record = await drafts.SendAsync(identifier, cancellationToken);

            return TypedResults.Ok(
                new ClientDraftSendResponse(draftId, record.Id.Value, record.Stage.ToString()));
        }
        catch (MailDraftRefusedException refusal)
        {
            return Refused(refusal);
        }
        catch (OutgoingMailRefusedException refusal)
        {
            // A conflict rather than a bad request: nothing about the request is wrong and no rewriting of it by the
            // client would help — what refused is a rule of this deployment about the message the owner already wrote.
            return Coded(refusal, StatusCodes.Status409Conflict);
        }
        catch (SensitiveContentScannerUnavailableException refusal)
        {
            // The one refusal here that is temporary, and the only one worth retrying: the message was neither
            // refused nor sent, because what would have judged it could not be reached.
            return Coded(refusal, StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>Stages one file against a draft the acting owner is writing.</summary>
    /// <param name="draftId">The draft the file is attached to.</param>
    /// <param name="fileName">What the file is called, which is the author's own text and is never read as a path.</param>
    /// <param name="attachments">Takes the file in, for a caller the write's own grant admits.</param>
    /// <param name="context">The request being answered, whose body carries the octets.</param>
    /// <param name="cancellationToken">Cancels the read of the body and the write.</param>
    /// <returns><c>200</c> with the staged file, <c>404</c> where this owner holds no such draft still being written, or <c>400</c> naming the bound the file exceeded.</returns>
    /// <remarks>
    /// <para>
    /// The octets are the request body and nothing else, so what a client uploads is what is staged: no form to parse,
    /// no boundary to trust, and no second copy of the file made to find it. What the file declares itself to be is
    /// the request's own <c>Content-Type</c>, taken without its parameters, and a request declaring none is read as
    /// the general binary type rather than having its content examined.
    /// </para>
    /// <para>
    /// <b>Cancelling an upload leaves nothing behind.</b> A request the author abandoned mid-transfer never reaches
    /// the write at all, and one already taken in is removed by naming it — neither leaves octets this deployment
    /// holds for a message nobody is writing.
    /// </para>
    /// </remarks>
    internal static async Task<Results<Ok<ClientDraftAttachmentResponse>, ProblemHttpResult>> StageAttachmentAsync(
        [FromRoute] Guid draftId,
        [FromQuery] string? fileName,
        [FromServices] MailDraftAttachments attachments,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        ArgumentNullException.ThrowIfNull(context);

        if (HeldDraft(draftId) is not { } identifier)
        {
            return NoDraftNamed();
        }

        using var buffer = new MemoryStream();

        await context.Request.Body.CopyToAsync(buffer, cancellationToken);

        if (buffer.Length == 0)
        {
            return Refuse("An upload carries the file. A request with no body attaches nothing.");
        }

        var file = new AuthoredEmailAttachment(
            fileName ?? string.Empty,
            DeclaredMediaType(context.Request.ContentType),
            buffer.ToArray());

        try
        {
            return TypedResults.Ok(
                ClientDraftAttachmentResponse.For(
                    await attachments.StageAsync(identifier, file, cancellationToken)));
        }
        catch (MailDraftRefusedException refusal)
        {
            return Refused(refusal);
        }
    }

    /// <summary>Takes one staged file back off a draft the acting owner is writing.</summary>
    /// <param name="draftId">The draft the file was attached to.</param>
    /// <param name="attachmentId">The file to take off.</param>
    /// <param name="attachments">Performs the removal, for a caller the write's own grant admits.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns><c>204</c> whether or not the draft carried such a file, or <c>404</c> where this owner holds no such draft still being written.</returns>
    /// <remarks>
    /// Taking a file off twice is one removal and the second answers as the first did, because the outcome a caller
    /// asked for holds either way. The stored message still carries the file until the next revision is composed, for
    /// the reason staging one does not put it there.
    /// </remarks>
    internal static async Task<Results<NoContent, ProblemHttpResult>> UnstageAttachmentAsync(
        [FromRoute] Guid draftId,
        [FromRoute] Guid attachmentId,
        [FromServices] MailDraftAttachments attachments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attachments);

        if (HeldDraft(draftId) is not { } identifier)
        {
            return NoDraftNamed();
        }

        if (attachmentId == Guid.Empty)
        {
            return Refuse("The request named no staged file. Name the identifier the upload reported for it.");
        }

        try
        {
            await attachments.UnstageAsync(
                identifier,
                MailDraftAttachmentId.Create(attachmentId),
                cancellationToken);

            return TypedResults.NoContent();
        }
        catch (MailDraftRefusedException refusal)
        {
            return Refused(refusal);
        }
    }

    /// <summary>Writes the draft the request describes, as a new one or as the next version of one that exists.</summary>
    /// <remarks>
    /// The two shapes are read here rather than on two routes, because a revision has to be able to stay whichever
    /// shape the draft already is. Which of them the request describes is the pair of answered-message fields: both
    /// present is an answer, neither is a message of its own, and one without the other names nothing at all.
    /// </remarks>
    private static async Task<Results<Ok<ClientDraftResponse>, ProblemHttpResult>> SaveAsync(
        ClientDraftWriteRequest? request,
        MailDraftId? revises,
        AuthoredMailDrafting drafting,
        AuthoredResponseDrafting answering,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(drafting);
        ArgumentNullException.ThrowIfNull(answering);

        if (request is null)
        {
            return Refuse("A saved draft carries the message the author wrote.");
        }

        try
        {
            var draft = (request.AnsweredEmailId, request.Answers) switch
            {
                ({ } answered, { } answers) =>
                    await answering.SaveAsync(
                        AnswerRequest(request, answered, answers, revises),
                        cancellationToken),
                (null, null) =>
                    await drafting.SaveAsync(OwnMessageRequest(request, revises), cancellationToken),

                // One of the pair without the other names no answer: three answers to one message reach three
                // different sets of people, and an answer to nothing is a message of its own written the wrong way.
                _ => throw MailDraftRefusedException.AnsweredEmailAndAnswerDisagree(),
            };

            return TypedResults.Ok(ClientDraftResponse.For(draft));
        }
        catch (MailDraftRefusedException refusal)
        {
            return Refused(refusal);
        }
        catch (MailAccountNotAccessibleException)
        {
            return Refuse("The account is not one this owner owns.");
        }
    }

    /// <summary>Composes the request for a draft of a message of its own, which states its own account and subject.</summary>
    /// <exception cref="MailDraftRefusedException">Thrown when the request states neither, or names an account no account could be named by.</exception>
    private static MailDraftRequest OwnMessageRequest(
        ClientDraftWriteRequest request,
        MailDraftId? revises)
    {
        if (request.Account is null || request.Subject is null)
        {
            throw MailDraftRefusedException.MessageNotStated();
        }

        if (!TryReadAccount(request.Account, out var account) || account is not { } named)
        {
            throw MailDraftRefusedException.AccountNotNamed();
        }

        return new MailDraftRequest
        {
            Account = named,
            Recipients = NamedRecipients(request),
            Subject = request.Subject,
            PlainTextBody = request.PlainTextBody ?? string.Empty,
            HtmlBody = request.HtmlBody,
            Author = Author(),
            Revises = revises,
        };
    }

    /// <summary>Composes the request for a draft answering a stored message, which reads its account and subject from it.</summary>
    /// <exception cref="MailDraftRefusedException">Thrown when the request states an account or a subject of its own, or names an answer this system does not publish.</exception>
    private static MailResponseDraftRequest AnswerRequest(
        ClientDraftWriteRequest request,
        Guid answeredEmailId,
        string answers,
        MailDraftId? revises)
    {
        if (request.Account is not null || request.Subject is not null)
        {
            throw MailDraftRefusedException.AnsweredDraftStatesItsOwnMessage();
        }

        if (answeredEmailId == Guid.Empty || AnsweredAct(answers) is not { } act)
        {
            throw MailDraftRefusedException.AnswerUnknown();
        }

        return new MailResponseDraftRequest
        {
            AnsweredEmailId = StoredEmailId.Create(answeredEmailId),
            Act = act,
            PlainTextBody = request.PlainTextBody ?? string.Empty,
            HtmlBody = request.HtmlBody,
            Recipients = NamedRecipients(request),
            Author = Author(),
            Revises = revises,
        };
    }

    /// <summary>Reads the three recipient headers the request carries into the one list every author writes.</summary>
    private static IReadOnlyList<NamedRecipient> NamedRecipients(ClientDraftWriteRequest request) =>
        AuthoredRecipientHeaders.NamedRecipients(
            request.To,
            request.Cc,
            request.Bcc,
            MailDraftRefusedException.TooManyRecipients,
            MailDraftRefusedException.From);

    /// <summary>Reads the authored act the published name states.</summary>
    /// <remarks>
    /// A closed mapping rather than a parse of the enumeration's own member names, for the reason the timeline's order
    /// is one: a parse would also accept a number and a comma-separated list, neither of which any client wrote.
    /// </remarks>
    private static AuthoredResponseAct? AnsweredAct(string answers) => answers switch
    {
        _ when NamesTheSame(answers, SenderOnlyAnswer) => AuthoredResponseAct.Reply,
        _ when NamesTheSame(answers, EveryoneAnswer) => AuthoredResponseAct.ReplyToAll,
        _ when NamesTheSame(answers, ForwardAnswer) => AuthoredResponseAct.Forward,
        _ => null,
    };

    /// <summary>Reports whether a caller wrote one of this surface's published names.</summary>
    private static bool NamesTheSame(string written, string published) =>
        string.Equals(written, published, StringComparison.OrdinalIgnoreCase);

    /// <summary>Names the act writing this draft down, which is provenance rather than an identity to compare.</summary>
    /// <remarks>
    /// A draft carries no idempotency key and takes none from a caller, for the reason the tool surface's drafting
    /// takes none: asking twice for a draft is two drafts, and the second costs an owner a deletion rather than a
    /// recipient a second message. So the identity is minted per call and says what it truly is — one act.
    /// </remarks>
    private static OutgoingEmailRequester Author() =>
        OutgoingEmailRequester.Command(Guid.NewGuid().ToString());

    /// <summary>Reads the draft a route named, keeping an identifier that names nothing apart from a draft nobody holds.</summary>
    private static MailDraftId? HeldDraft(Guid draftId) =>
        draftId == Guid.Empty ? null : MailDraftId.Create(draftId);

    /// <summary>Reads the text a request names an account by, refusing text no name of this system is spelled with.</summary>
    private static bool TryReadAccount(string? account, out MailAccountSelector? named)
    {
        named = null;

        if (string.IsNullOrWhiteSpace(account))
        {
            return true;
        }

        try
        {
            named = MailAccountSelector.Create(account);

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Reads what a part declares itself to be, without the parameters that say how it is written.</summary>
    /// <remarks>
    /// A charset or a boundary belongs to the transfer rather than to the file, and the composition writes its own; the
    /// media type is the author's statement about what the octets are, which is the whole of what is kept.
    /// </remarks>
    private static string DeclaredMediaType(string? contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out var parsed) && parsed.MediaType.HasValue
            ? parsed.MediaType.Value!
            : AttachmentContentResponse.FallbackMediaType;

    /// <summary>Answers a refusal a draft act raised, at the status the refusal's own code decides.</summary>
    /// <remarks>
    /// A draft this owner does not hold is the absence of the thing addressed, so it is <c>404</c>; screening refusing
    /// a message is a rule of this deployment about what the author already wrote, so it is <c>409</c>; and everything
    /// else is something the author can change, so it is <c>400</c>. The code travels beside every one of them, so a
    /// client matches the failure rather than parsing the sentence.
    /// </remarks>
    private static ProblemHttpResult Refused(MailDraftRefusedException refusal) => Coded(
        refusal,
        refusal.ErrorCode == MailFathomErrorCode.MailDraftNotFound
            ? StatusCodes.Status404NotFound
            : refusal.ErrorCode == MailFathomErrorCode.OutgoingMailContentRefused
                || refusal.ErrorCode == MailFathomErrorCode.OutgoingMailNotFullyScanned
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest);

    /// <summary>Answers a failure with its own message and the code a client matches it by.</summary>
    private static ProblemHttpResult Coded(MailFathomException refusal, int statusCode) => TypedResults.Problem(
        refusal.Message,
        statusCode: statusCode,
        extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [RouteAuthorization.ErrorCodeExtension] = refusal.ErrorCode.Value,
        });

    /// <summary>States that the request named no draft to act on.</summary>
    private static ProblemHttpResult NoDraftNamed() => TypedResults.Problem(
        "The request named no draft. Name the identifier a draft reading reports for it.",
        statusCode: StatusCodes.Status404NotFound);

    /// <summary>States what a caller has to change, without echoing what they sent.</summary>
    private static ProblemHttpResult Refuse(string stated) =>
        TypedResults.Problem(stated, statusCode: StatusCodes.Status400BadRequest);
}
