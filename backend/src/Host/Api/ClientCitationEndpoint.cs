// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Citations;
using MailFathom.Application.Discovery.Presentation.Citations;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Follows the citations of a presentation plan to the mail they were drawn from.</summary>
/// <remarks>
/// <para>
/// One route for every citation a plan can declare, which is what makes a rendered answer checkable in two actions: a
/// reader presses the source beside a fact and is shown the paragraph it came from, rather than being taken to the top
/// of the message and left to search it. A client draws one evidence affordance for every block type because every
/// block's citations are followed here.
/// </para>
/// <para>
/// It takes the plan's own citation targets, unchanged, so a client posts back what it read rather than translating
/// between two spellings of one identity. The order is the contract: the answer carries one resolution per citation in
/// the order the request named them, and each names the message it answers for.
/// </para>
/// <para>
/// A source the caller may not read is a state rather than a refusal, and so is a place inside a message that is no
/// longer there. Neither fails the request, because a plan is shown to whoever holds it and a reader has to be able to
/// tell a fact whose source is private, or whose passage has been re-cut since, from an answer that is broken.
/// </para>
/// <para>
/// It is a <c>POST</c> because a citation target is a small document rather than a value: the three kinds carry
/// different members, and a request line of them would be an encoding of the plan's own JSON. Nothing here changes
/// anything, and no route on this surface reaches a mail server.
/// </para>
/// </remarks>
internal static class ClientCitationEndpoint
{
    /// <summary>The route citations are followed at, relative to the client prefix.</summary>
    internal const string CitationResolutionRoute = "/citations/resolution";

    /// <summary>The greatest size a submitted batch may have on the wire.</summary>
    /// <remarks>
    /// Generous against the count bound — a kind, a message identity, and either a passage identity or a small integer
    /// per citation — and small enough that a body is refused before it is read rather than after. The count is what
    /// bounds the work; this bounds what has to be buffered to find out the count.
    /// </remarks>
    internal const int MaxWriteRequestBytes = 16 * 1024;

    /// <summary>Maps the route into the client group, so it inherits the group's requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientCitations(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapPost(CitationResolutionRoute, ResolveAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailRead);
    }

    /// <summary>Follows every citation the request names, or reports what was wrong with the request itself.</summary>
    /// <param name="request">The citations to follow.</param>
    /// <param name="resolver">Follows them, for a caller the read's own grant admits.</param>
    /// <param name="cancellationToken">Cancels the reads when the client disconnects.</param>
    /// <returns><c>200</c> with one resolution per citation, <c>400</c> naming what was wrong with the request, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.read</c>.</returns>
    /// <remarks>
    /// Every refusal here is about the batch rather than about one citation, because a citation this boundary cannot
    /// read at all is one a client composed rather than one a plan declared: a malformed kind, a missing identity, or a
    /// position no message could hold says the caller sent something a plan never contained, and answering part of such
    /// a request would be answering a question nobody asked.
    /// </remarks>
    internal static async Task<Results<Ok<ClientCitationResolutionResponse>, ProblemHttpResult>> ResolveAsync(
        [FromBody] ClientCitationResolutionRequest? request,
        [FromServices] CitationResolver resolver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        if (request?.Citations is not { Count: > 0 } citations)
        {
            return Refusal("A resolution names at least one citation.");
        }

        if (citations.Count > CitationResolver.MaximumCitations)
        {
            return Refusal($"One request follows at most {CitationResolver.MaximumCitations} citations.");
        }

        var targets = new List<PresentationCitationTarget>(citations.Count);

        foreach (var citation in citations)
        {
            if (TargetOf(citation) is not { } target)
            {
                return Refusal(
                    "A citation names one of the kinds 'email', 'fragment', and 'attachment', the message it belongs "
                    + "to, and the passage or the attachment position that kind requires.");
            }

            targets.Add(target);
        }

        var resolved = await resolver.ResolveAsync(targets, cancellationToken);

        return TypedResults.Ok(ClientCitationResolutionResponse.For(resolved));
    }

    /// <summary>Reads one citation off the wire into the target it names, or reports that it names none.</summary>
    /// <param name="citation">The citation as the request body wrote it, which a JSON array may write as nothing at all.</param>
    /// <returns>The target it names, or <see langword="null" /> where the document is not one of the three.</returns>
    /// <remarks>
    /// The kinds and their required members are the plan contract's own, so a client sends a citation target back
    /// exactly as the plan published it. What is checked here is that the document is one of the three, because a
    /// request body is untrusted whatever the plan it claims to have come from said — including the entry a serializer
    /// will happily read as <see langword="null" /> however the list is declared.
    /// </remarks>
    internal static PresentationCitationTarget? TargetOf(ClientCitationRequest? citation)
    {
        if (citation?.Email is not { } email || email == Guid.Empty)
        {
            return null;
        }

        var storedEmailId = StoredEmailId.Create(email);

        return citation.Kind switch
        {
            EmailCitationTarget.Kind when citation is { Fragment: null, AttachmentPosition: null } =>
                new EmailCitationTarget(storedEmailId),
            FragmentCitationTarget.Kind when citation is { Fragment: { } fragment, AttachmentPosition: null }
                && fragment != Guid.Empty =>
                new FragmentCitationTarget(storedEmailId, EmailChunkId.Create(fragment)),
            AttachmentCitationTarget.Kind when citation is { Fragment: null, AttachmentPosition: >= 0 } =>
                new AttachmentCitationTarget(storedEmailId, citation.AttachmentPosition.Value),
            _ => null,
        };
    }

    private static ProblemHttpResult Refusal(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status400BadRequest);
}

/// <summary>The citations one request asks to have followed.</summary>
/// <param name="Citations">The citations, in the order the answer will report them.</param>
internal sealed record ClientCitationResolutionRequest(IReadOnlyList<ClientCitationRequest> Citations);

/// <summary>One citation target, spelled as the presentation plan publishes it.</summary>
/// <param name="Kind">Which of the three targets this is: <c>email</c>, <c>fragment</c>, or <c>attachment</c>.</param>
/// <param name="Email">The message the citation is followed to, which every kind carries.</param>
/// <param name="Fragment">The passage a fragment citation points at, and absent for the other two kinds.</param>
/// <param name="AttachmentPosition">The zero-based position an attachment citation points at, and absent for the other two kinds.</param>
/// <remarks>
/// Flat rather than a discriminated document of its own, while accepting exactly the JSON the plan writes: the members
/// a kind does not use are absent, which is what the plan's own serialization produces, and reading them as optional
/// keeps the schema this surface publishes a single object rather than three a client would have to choose between.
/// Which members a kind requires is enforced where the target is composed.
/// </remarks>
internal sealed record ClientCitationRequest(string? Kind, Guid? Email, Guid? Fragment, int? AttachmentPosition);

/// <summary>What following one request's citations produced.</summary>
/// <param name="Citations">One resolution per citation, in the order the request named them.</param>
internal sealed record ClientCitationResolutionResponse(IReadOnlyList<ClientResolvedCitationResponse> Citations)
{
    /// <summary>Describes what the use case resolved for the wire.</summary>
    /// <param name="resolved">The resolutions, in the order the request named their citations.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resolved" /> is <see langword="null" />.</exception>
    internal static ClientCitationResolutionResponse For(IReadOnlyList<ResolvedCitation> resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);

        return new ClientCitationResolutionResponse([.. resolved.Select(ClientResolvedCitationResponse.For)]);
    }
}

/// <summary>One citation as this surface answers for it.</summary>
/// <param name="StoredEmailId">The message the citation named, which is carried whatever became of it.</param>
/// <param name="Outcome">What became of the citation, as the outcome's own name.</param>
/// <param name="Message">The message the citation belongs to, or <see langword="null" /> for a source the caller may not read.</param>
/// <param name="Fragment">The passage the citation points at, or <see langword="null" /> where it points at none or that passage is gone.</param>
/// <param name="Attachment">The file the citation points at, or <see langword="null" /> where it points at none or that file is gone.</param>
/// <remarks>
/// The message travels with an unresolvable place on purpose, so a client draws the source of a fact whose passage has
/// been re-cut since the plan was composed rather than dropping the citation. All of it is mail content and personal
/// data, so none of it reaches a log, a span attribute, or a telemetry event.
/// </remarks>
internal sealed record ClientResolvedCitationResponse(
    Guid StoredEmailId,
    string Outcome,
    ClientCitedMessageResponse? Message,
    ClientCitedFragmentResponse? Fragment,
    ClientCitedAttachmentResponse? Attachment)
{
    /// <summary>Describes one resolution for the wire.</summary>
    /// <param name="resolved">The resolution the use case produced.</param>
    /// <returns>The response body.</returns>
    internal static ClientResolvedCitationResponse For(ResolvedCitation resolved) => new(
        resolved.StoredEmailId.Value,
        resolved.Outcome.ToString(),
        resolved.Message is { } message ? ClientCitedMessageResponse.For(message) : null,
        resolved.Fragment is { } fragment ? ClientCitedFragmentResponse.For(fragment) : null,
        resolved.Attachment is { } attachment ? ClientCitedAttachmentResponse.For(attachment) : null);
}

/// <summary>The message one resolved citation belongs to.</summary>
/// <param name="StoredEmailId">The message, as every other client route names it.</param>
/// <param name="Account">The configured account it was read from.</param>
/// <param name="Folder">MailFathom's own name for the folder it was read from.</param>
/// <param name="Subject">The decoded subject, or <see langword="null" /> where the message carried none.</param>
/// <param name="SentAt">When the sender says it was sent, or <see langword="null" /> where it wrote no usable date.</param>
/// <param name="ReceivedAt">When the last receiving hop recorded it, or <see langword="null" /> where no header carried a usable date.</param>
/// <remarks>
/// Enough to draw a source where it stands and no more. A client that wants the rest of the message asks the message
/// route for it, which is the request a reader chose to make.
/// </remarks>
internal sealed record ClientCitedMessageResponse(
    Guid StoredEmailId,
    string Account,
    string Folder,
    string? Subject,
    DateTimeOffset? SentAt,
    DateTimeOffset? ReceivedAt)
{
    /// <summary>Describes one cited message for the wire.</summary>
    /// <param name="message">The message the resolution carried.</param>
    /// <returns>The response body.</returns>
    internal static ClientCitedMessageResponse For(CitedMessage message) => new(
        message.StoredEmailId.Value,
        message.AccountId.Value,
        message.FolderAlias.Value,
        message.Subject,
        message.SentAt,
        message.ReceivedAt);
}

/// <summary>The passage one resolved citation points at.</summary>
/// <param name="FragmentId">The passage, as the citation named it.</param>
/// <param name="Ordinal">Its position in the message, counted from zero in reading order.</param>
/// <param name="StartOffset">Where it begins in the extracted text it was cut from.</param>
/// <param name="EndOffset">Where it ends in that text, one past its last character.</param>
/// <param name="Text">The passage itself, which is what a reader checks the fact against.</param>
/// <remarks>
/// The offsets are published beside the text because they are what makes the reference verifiable, and what lets a
/// client that opens the whole message afterwards land on the place the fact came from.
/// </remarks>
internal sealed record ClientCitedFragmentResponse(
    Guid FragmentId,
    int Ordinal,
    int StartOffset,
    int EndOffset,
    string Text)
{
    /// <summary>Describes one cited passage for the wire.</summary>
    /// <param name="fragment">The passage the resolution carried.</param>
    /// <returns>The response body.</returns>
    internal static ClientCitedFragmentResponse For(CitedFragment fragment) => new(
        fragment.Fragment.Value,
        fragment.Ordinal,
        fragment.StartOffset,
        fragment.EndOffset,
        fragment.Text);
}

/// <summary>The file one resolved citation points at, described and carrying none of what it holds.</summary>
/// <param name="Position">The zero-based place the file holds, which is what the attachment route is asked with.</param>
/// <param name="FileName">The normalized file name, or <see langword="null" /> where the part carried no usable name.</param>
/// <param name="WasFileNameNormalized">Whether normalization had to rewrite what the message wrote.</param>
/// <param name="MediaType">What the part declares itself to be, which is what the sender wrote rather than a reading of the content.</param>
/// <param name="SizeOctets">How many octets the file holds once its transfer encoding is decoded.</param>
/// <remarks>
/// The members are the message route's own, so a client draws a cited file with the component it already draws the
/// attachment strip with rather than with a second one — which is why the rewritten-name flag travels here as well: the
/// name a reader is shown is the same name, and it is worth drawing carefully in both places.
/// </remarks>
internal sealed record ClientCitedAttachmentResponse(
    int Position,
    string? FileName,
    bool WasFileNameNormalized,
    string MediaType,
    long SizeOctets)
{
    /// <summary>Describes one cited file for the wire.</summary>
    /// <param name="attachment">The file the resolution carried.</param>
    /// <returns>The response body.</returns>
    internal static ClientCitedAttachmentResponse For(CitedAttachment attachment) => new(
        attachment.Position,
        attachment.FileName,
        attachment.WasFileNameNormalized,
        attachment.MediaType,
        attachment.SizeOctets);
}
