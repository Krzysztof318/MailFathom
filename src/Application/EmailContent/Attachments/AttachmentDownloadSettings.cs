// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Attachments;

/// <summary>Where an attachment download link points, and how long one stays redeemable.</summary>
/// <param name="DownloadAddressPrefix">
/// The absolute address a capability is appended to, already ending in a slash, or <see langword="null" /> when this
/// deployment declares no public address.
/// </param>
/// <param name="LinkLifetime">How long a minted link stays redeemable.</param>
/// <remarks>
/// <para>
/// The address is declared rather than derived. A URL composed from a request's <c>Host</c> header would let whoever
/// called the tool decide where the link it receives points, which turns a capability this deployment issued into one an
/// attacker addressed. A deployment that declares none issues no link at all, which is the state
/// <see cref="IAttachmentDownloadLinkIssuer.CanIssueLinks" /> reports and the tool result publishes.
/// </para>
/// <para>
/// It arrives with the download route already on it, composed by the composition root that owns both the declared
/// address and the route. That keeps the one place a URL path is written the same place it is mapped, so the address a
/// link points at and the address the process answers cannot drift apart.
/// </para>
/// <para>
/// The lifetime is the whole of a link's revocation model, so the bounds on it are the product's rather than the
/// operator's: a window measured in minutes makes a leaked URL usually already dead, and one measured in days would make
/// it a durable credential written into proxy logs, browser history, and chat transcripts by software nobody here
/// controls. The host validates a configured value against a stated floor and ceiling before this record is built.
/// </para>
/// </remarks>
public sealed record AttachmentDownloadSettings(Uri? DownloadAddressPrefix, TimeSpan LinkLifetime);
