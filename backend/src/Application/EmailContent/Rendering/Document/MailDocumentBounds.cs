// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Rendering.Document;

/// <summary>What one reduction of one body may produce at most.</summary>
/// <remarks>
/// <para>
/// Every bound here is a bound on hostile input. A message is written by a stranger, so the shape of the document it
/// reduces to is the sender's decision and the cost of drawing it is the reader's — which is what makes each of these a
/// safety limit rather than a tuning knob. A body that reaches one is reduced as far as the bound and reports itself as
/// truncated rather than being refused, because half a newsletter is worth more to a reader than none of it.
/// </para>
/// <para>
/// They are values rather than configuration for the same reason the block catalogue is closed: a deployment that could
/// raise the nesting depth could be persuaded to raise it, and nothing an operator gains from that is worth the reading
/// pane recursing on a message somebody sent them.
/// </para>
/// </remarks>
public sealed record MailDocumentBounds
{
    /// <summary>Gets the bounds every reduction runs under.</summary>
    public static MailDocumentBounds Default { get; } = new();

    /// <summary>Gets how deeply blocks may nest before the reduction stops descending.</summary>
    /// <remarks>
    /// Mail layout tables nest three or four deep in ordinary newsletters and a quoted exchange adds a level per reply,
    /// so the bound is set well past both and far below what would cost a pane its stack.
    /// </remarks>
    public int MaximumDepth { get; init; } = 24;

    /// <summary>Gets how many blocks one document may hold, counting the ones inside other blocks.</summary>
    public int MaximumBlocks { get; init; } = 4000;

    /// <summary>Gets how many runs one paragraph or heading may hold.</summary>
    public int MaximumRunsPerBlock { get; init; } = 512;

    /// <summary>Gets how many characters one run may hold.</summary>
    public int MaximumCharactersPerRun { get; init; } = 20_000;

    /// <summary>Gets how many rows one table may hold.</summary>
    public int MaximumTableRows { get; init; } = 1000;

    /// <summary>Gets how many cells one row may hold.</summary>
    public int MaximumTableCells { get; init; } = 64;

    /// <summary>Gets how many pictures of its own one message may have inlined.</summary>
    public int MaximumInlineImages { get; init; } = 64;

    /// <summary>Gets how many octets one inlined picture may hold before it is left undrawn.</summary>
    /// <remarks>
    /// The bound on the part rather than on what it becomes: a <c>data:</c> URI is a third longer than the octets behind
    /// it, and the number worth stating is the one about the message. A picture past it is reported as undrawn rather
    /// than replaced by a reference, because a reference is the thing this path exists not to carry.
    /// </remarks>
    public int MaximumInlineImageOctets { get; init; } = 2 * 1024 * 1024;

    /// <summary>Gets how many octets of its own pictures one document may carry in total.</summary>
    /// <remarks>
    /// The bound on the picture is about one part and this one is about the answer, which is the number a client sizes
    /// its read against: without it a message carrying the permitted count of the permitted size would compose a
    /// response two orders of magnitude past anything a reading pane will buffer, and the pane would lose the whole
    /// message — its text included — rather than one photograph. A picture past this is reported as undrawn exactly as
    /// one past the per-picture bound is, so what the reader loses is stated rather than silent.
    /// </remarks>
    public int MaximumInlineImageOctetsPerDocument { get; init; } = 4 * 1024 * 1024;
}
