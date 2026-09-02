// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Access;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.Emails.DownloadAttachment;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Endpoints;
using MailFathom.Host.Security.Transport;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves one attachment to whoever presents a valid capability for it.</summary>
/// <remarks>
/// <para>
/// The one route in this process that answers without a credential, and deliberately so. A link exists to be handed to
/// whatever actually fetches files — a browser, a downloader, a client's own HTTP stack — and none of those can attach
/// the MCP endpoint's credential, so requiring one would make the capability unusable by the only callers it was minted
/// for. The signature is therefore the whole of the access control, and everything around it is sized as such: the
/// capability names one attachment of one email, it expires within minutes, its tag is verified in fixed time, and the
/// mailbox is read afresh on every redemption so a link cannot outlive the deletion of its own message.
/// </para>
/// <para>
/// It belongs to the MCP surface rather than to a surface of its own. That is what gives it that endpoint's transport,
/// its rate limiting, and its enablement without a third externally reachable listener existing: a deployment that
/// serves no MCP endpoint serves no download route either, which is the same answer an operator already expects.
/// </para>
/// <para>
/// Every refusal is one refusal. An expired capability, a forged one, one naming an email this deployment no longer
/// serves, one whose local copy is damaged, and one naming an attachment the message does not carry are all
/// <c>404</c> with the same body, because telling them apart would let whoever holds a capability learn what became of
/// mail they can no longer read. Nothing about a request or a response is logged here: the capability is an
/// unauthenticated way to obtain mail content, and the file name and octets are mail content themselves.
/// </para>
/// </remarks>
internal static class EmailAttachmentDownloadEndpoint
{
    /// <summary>The path prefix the route is served beneath, which is also what a minted link is built from.</summary>
    internal const string RoutePrefix = "/attachments";

    /// <summary>The one thing a refused request is told, whatever the reason was.</summary>
    internal const string RefusalDetail = "This attachment link is not valid.";

    /// <summary>Maps the download route.</summary>
    /// <param name="endpoints">The application's route builder.</param>
    /// <returns>The mapped route, so the caller can attach the surface's own metadata to it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoints" /> is <see langword="null" />.</exception>
    internal static RouteHandlerBuilder MapEmailAttachmentDownload(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints.MapGet($"{RoutePrefix}/{{capability}}", DownloadAsync);
    }

    /// <summary>Verifies the presented capability and streams the attachment it authorizes.</summary>
    /// <param name="capability">The capability the URL carried, which is entirely untrusted.</param>
    /// <param name="ticketReader">Verifies the capability against the deployment's key ring.</param>
    /// <param name="downloadReader">Opens the attachment the verified capability names.</param>
    /// <param name="principals">Carries what authorized this request into the application layer.</param>
    /// <param name="deploymentOwner">Names the owner whose mail a redeemed capability reaches.</param>
    /// <param name="context">The request being answered, whose response body the attachment is written to.</param>
    /// <param name="cancellationToken">Cancels the read when the reader disconnects.</param>
    /// <returns>The attachment's octets, <c>404</c> with a body that says nothing about why, or <c>409</c> where this deployment has no sole owner for a ticket naming nobody.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any resolved dependency is <see langword="null" />.</exception>
    /// <remarks>
    /// The verified ticket is what the request runs under, and it is stated onto the scope before the use case is
    /// reached. Nothing authenticated here, so without that statement the use case would be reached under no principal
    /// and would refuse — which is the same rule that makes an entrypoint added later say what admitted it rather than
    /// inherit a permission from somewhere. The principal states an owner beside the capability, because the read behind
    /// it is bounded to one owner's accounts like every other mail read rather than to the deployment's.
    /// </remarks>
    internal static async Task<Results<EmptyHttpResult, NotFound<ProblemDetails>, ProblemHttpResult>> DownloadAsync(
        string capability,
        IAttachmentDownloadTicketReader ticketReader,
        EmailAttachmentDownloadReader downloadReader,
        TransportAuthorizedPrincipalSource principals,
        IDeploymentMailOwnerSource deploymentOwner,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticketReader);
        ArgumentNullException.ThrowIfNull(downloadReader);
        ArgumentNullException.ThrowIfNull(principals);
        ArgumentNullException.ThrowIfNull(deploymentOwner);
        ArgumentNullException.ThrowIfNull(context);

        var ticket = await ticketReader.RedeemAsync(capability, cancellationToken);
        if (ticket is null)
        {
            return Refused();
        }

        // The owner comes from the deployment rather than from the ticket, which is exact on a deployment holding one
        // owner and has no answer on one holding several: the capability is a signed ticket rather than a credential, so
        // nothing presented here names a person. Such a deployment refuses the download rather than guessing whose mail
        // it is, and the refusal is composed by the same helper the route groups' filter uses — this route is mapped
        // outside every group deliberately, so nothing else would classify it and the caller would meet an unhandled
        // fault carrying the capability into a framework log. Recording the owner in the ticket is what ends the
        // refusal itself, and it changes the capability's own format.
        MailOwnerId owner;

        try
        {
            owner = deploymentOwner.Owner;
        }
        catch (DeploymentMailOwnerUnresolvedException unattributable)
        {
            return RouteAuthorization.Unattributable(unattributable);
        }

        principals.Assume(AuthorizedPrincipal.SignedCapability(owner, AuthorizedObjectOf(ticket)));

        await using var attachment = await downloadReader.OpenAsync(ticket, cancellationToken);
        if (attachment is null)
        {
            return Refused();
        }

        AttachmentContentResponse.Describe(context.Response, attachment.Description);

        await attachment.WriteContentToAsync(context.Response.Body, cancellationToken);

        return TypedResults.Empty;
    }

    /// <summary>Names the one object the signature was bounded to, in MailFathom's own identifiers.</summary>
    /// <remarks>
    /// It is what a record of a refusal names the work by, so it carries the stored email's identity and the position
    /// within it and nothing from the message: a file name, a media type, or a subject would be mail content.
    /// </remarks>
    private static string AuthorizedObjectOf(AttachmentDownloadTicket ticket) => string.Create(
        CultureInfo.InvariantCulture,
        $"{RoutePrefix}/{ticket.StoredEmailId.Value}/{ticket.AttachmentPosition}");

    private static NotFound<ProblemDetails> Refused() =>
        TypedResults.NotFound(new ProblemDetails
        {
            Title = "Not found",
            Detail = RefusalDetail,
            Status = StatusCodes.Status404NotFound,
        });
}
