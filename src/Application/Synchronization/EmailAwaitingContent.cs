// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Synchronization;

/// <summary>One occurrence a run recorded without its content, with what a later run has to know to complete it.</summary>
/// <param name="Metadata">What the occurrence was stored with, which is what a fetch of it has to be recorded under.</param>
/// <param name="IsFiledCopy">Whether the stored email is already joined to the outgoing send it is a copy of.</param>
/// <remarks>
/// The join travels beside the metadata because it decides one thing the completing run would otherwise get wrong:
/// whether the message is offered for a spam verdict. It is read from the stored email rather than from the filing that
/// created it, because that filing was met and settled by the discovery which recorded this occurrence, and the durable
/// join is what survives it.
/// </remarks>
public sealed record EmailAwaitingContent(RemoteEmailMetadata Metadata, bool IsFiledCopy);
