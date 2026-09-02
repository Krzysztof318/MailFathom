// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Rendering.Document;

/// <summary>A message's HTML body reduced to the closed document tree a pane draws natively.</summary>
/// <param name="SchemaVersion">Which revision of this contract the document was written against.</param>
/// <param name="Blocks">The blocks, in reading order, which is empty for a refused body.</param>
/// <param name="Refusal">Why the body is read as its plain text instead, or that it is not.</param>
/// <param name="RemovedRemoteReferenceCount">How many references to somebody else's server were removed rather than carried.</param>
/// <param name="RetainedRemoteImageCount">How many remote pictures the reader asked for and this document therefore carries.</param>
/// <param name="InlineImageCount">How many pictures were resolved from the message's own parts and inlined.</param>
/// <param name="UndrawnInlineImageCount">How many of the message's own pictures were left undrawn because they were beyond the bound.</param>
/// <param name="Truncated">Whether the reduction stopped at a bound rather than at the end of the body.</param>
/// <remarks>
/// <para>
/// This is what reading a message means on the default path, and its isolation statement is an absence rather than a
/// policy: the client receives no markup and runs no engine, so script cannot run because nothing that could run it is
/// here, and a remote resource cannot be fetched because — unless the reader asked otherwise — there is no remote
/// reference in the document to fetch. A rendering defect therefore cannot leak by fetching.
/// </para>
/// <para>
/// The counts are what the reader is told instead of being handed a blocked reference. Nothing here is a placeholder
/// pointing at a removed address and nothing is a flag telling a renderer to abstain: the addresses are gone, and the
/// number is what says something was there.
/// </para>
/// <para>
/// A link is the one sender-controlled absolute address the default carries on purpose, because showing a reader where
/// a link goes before they follow it needs the address. That is not a licence to resolve one for any other purpose.
/// </para>
/// <para>
/// The whole of this is mail. It is personal data of the reader and of whoever wrote to them, so no part of it reaches
/// a log line, a span attribute, an exception message, or a telemetry event.
/// </para>
/// </remarks>
public sealed record MailDocument(
    int SchemaVersion,
    IReadOnlyList<MailDocumentBlock> Blocks,
    MailDocumentRefusal Refusal,
    int RemovedRemoteReferenceCount,
    int RetainedRemoteImageCount,
    int InlineImageCount,
    int UndrawnInlineImageCount,
    bool Truncated)
{
    /// <summary>The revision of this contract that this build writes and reads.</summary>
    /// <remarks>
    /// It moves when the shape of the document itself moves; a block's own version moves when that block's shape does,
    /// and for no other reason. Neither is the application's version.
    /// </remarks>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Creates the document of a body that was reduced.</summary>
    /// <param name="blocks">The blocks, in reading order.</param>
    /// <param name="removedRemoteReferenceCount">How many references to somebody else's server were removed.</param>
    /// <param name="retainedRemoteImageCount">How many remote pictures the reader asked for.</param>
    /// <param name="inlineImageCount">How many of the message's own pictures were inlined.</param>
    /// <param name="undrawnInlineImageCount">How many of the message's own pictures were beyond the bound.</param>
    /// <param name="truncated">Whether the reduction stopped at a bound.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="blocks" /> is <see langword="null" />.</exception>
    public static MailDocument Reduced(
        IReadOnlyList<MailDocumentBlock> blocks,
        int removedRemoteReferenceCount,
        int retainedRemoteImageCount,
        int inlineImageCount,
        int undrawnInlineImageCount,
        bool truncated)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        return new MailDocument(
            CurrentSchemaVersion,
            blocks,
            blocks.Count == 0 ? MailDocumentRefusal.NothingRenderable : MailDocumentRefusal.None,
            removedRemoteReferenceCount,
            retainedRemoteImageCount,
            inlineImageCount,
            undrawnInlineImageCount,
            truncated);
    }

    /// <summary>Creates the document of a body the pane reads as plain text instead, and says why.</summary>
    /// <param name="refusal">Which of the three reasons it was.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the reason given is that nothing was refused.</exception>
    public static MailDocument Refused(MailDocumentRefusal refusal)
    {
        ArgumentOutOfRangeException.ThrowIfEqual((int)refusal, (int)MailDocumentRefusal.None);

        return new MailDocument(
            CurrentSchemaVersion,
            [],
            refusal,
            RemovedRemoteReferenceCount: 0,
            RetainedRemoteImageCount: 0,
            InlineImageCount: 0,
            UndrawnInlineImageCount: 0,
            Truncated: false);
    }
}
