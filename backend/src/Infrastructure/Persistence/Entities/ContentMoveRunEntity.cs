// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Move;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>The move of this deployment's stored content into the bucket, and how far its passes have come.</summary>
/// <remarks>
/// <para>
/// One row for the whole deployment, which the fixed key makes structural rather than a rule somebody has to remember.
/// The content backend is one setting for the instance, so there is no second scope a second move could cover, and two
/// operators asking together reach the same row and resolve to one move.
/// </para>
/// <para>
/// The position is kept here beside the counts rather than in a table of its own, unlike the re-derivation's, because
/// the two have the same lifetime: a payload that moved has left the walk's own set, so the cursor means nothing outside
/// the move that reached it and there is nothing for it to outlive.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class ContentMoveRunEntity
{
    /// <summary>The one key a move is ever written under, which is what makes a deployment have one move.</summary>
    internal const string DeploymentName = "stored-content";

    /// <summary>The greatest length the key column holds, which the one value above is far inside.</summary>
    internal const int MaximumNameLength = 64;

    public required string Name { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    public StoredContentMoveState State { get; set; }

    /// <summary>Gets or sets the payload kind the walk is currently inside.</summary>
    public EmailContentKind Kind { get; set; }

    /// <summary>Gets or sets the payload the last pass reached inside that kind, absent at the start of one.</summary>
    public Guid? ResumeAfter { get; set; }

    public long CopiedPayloadCount { get; set; }

    public long FailedPayloadCount { get; set; }

    public long MovedByteCount { get; set; }

    /// <summary>Gets or sets when the walk reached the end of the last payload kind, absent while it has not.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>Gets or sets the optimistic concurrency token, which is PostgreSQL's own <c>xmin</c> rather than a column.</summary>
    /// <remarks>
    /// An operator pausing the move and the pass committing what it just carried reach this row at the same moment. The
    /// token is what turns that into a conflict the retry policy resolves from a fresh read, instead of a pass writing
    /// its counts over a decision that was taken while it ran.
    /// </remarks>
    public uint ConcurrencyVersion { get; set; }
}
