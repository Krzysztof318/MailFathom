// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend.Mail;

/// <summary>One message's body as the deployment serves it, in both the renderings a pane draws from.</summary>
/// <param name="StoredEmailId">The message, as the request named it.</param>
/// <param name="Availability">Whether the body could be read at all, or why it could not, as the deployment's own name for the state.</param>
/// <param name="PlainText">The message as words, which is a rendering in its own right rather than a fallback that looks broken.</param>
/// <param name="Document">The message reduced to the document tree, or <see langword="null" /> where the body could not be read.</param>
/// <param name="RemoteImagesRequested">Whether this read was the one the reader asked remote pictures for.</param>
/// <remarks>
/// Everything here is mail, so it is personal data of the reader and of whoever wrote to them: it is not logged, not
/// written to local storage, not put in a telemetry event, and not repeated in a failure message.
/// </remarks>
public sealed record DeploymentMailBody(
    Guid StoredEmailId,
    string Availability,
    DeploymentMailBodyText PlainText,
    MailBodyDocument? Document,
    bool RemoteImagesRequested);

/// <summary>One textual rendering of a body, and what was left out of it.</summary>
/// <param name="Text">The text as it arrived, already bounded.</param>
/// <param name="OriginalCharacterCount">How many characters the message held before any bound was applied.</param>
/// <param name="Truncation">Which bound removed something, as the deployment's own name for it, or that none did.</param>
public sealed record DeploymentMailBodyText(string Text, int OriginalCharacterCount, string Truncation)
{
    /// <summary>Gets whether a bound removed something from this rendering.</summary>
    public bool WasTruncated => !string.Equals(this.Truncation, "None", StringComparison.Ordinal);
}

/// <summary>A message body reduced to the closed document tree the pane draws natively.</summary>
/// <param name="SchemaVersion">Which revision of the contract the deployment wrote.</param>
/// <param name="Blocks">The blocks, in reading order, which is empty for a refused body.</param>
/// <param name="Refusal">Why the pane reads this message as its plain text instead, or that it does not.</param>
/// <param name="RemovedRemoteReferenceCount">How many references to somebody else's server were removed rather than carried.</param>
/// <param name="RetainedRemoteImageCount">How many remote pictures the reader asked for and this document therefore carries.</param>
/// <param name="InlineImageCount">How many pictures were resolved from the message's own parts.</param>
/// <param name="UndrawnInlineImageCount">How many of the message's own pictures were left undrawn because they were beyond a bound.</param>
/// <param name="Truncated">Whether the reduction stopped at a bound rather than at the end of the body.</param>
/// <remarks>
/// <para>
/// Unless the reader asked otherwise, there is no remote reference in it to fetch — which is what defeats a tracking
/// pixel here rather than any setting this client honours. The counts are what the pane tells the reader instead.
/// </para>
/// <para>
/// The schema version is read rather than assumed. A deployment ahead of this build is ordinary, since a desktop head
/// and a deployment are updated separately, and what a pane does about it is say so and draw what it can.
/// </para>
/// </remarks>
public sealed record MailBodyDocument(
    int SchemaVersion,
    IReadOnlyList<MailBodyBlock> Blocks,
    MailBodyRefusal Refusal,
    int RemovedRemoteReferenceCount,
    int RetainedRemoteImageCount,
    int InlineImageCount,
    int UndrawnInlineImageCount,
    bool Truncated)
{
    /// <summary>The revision of the contract this build implements.</summary>
    public const int ImplementedSchemaVersion = 1;

    /// <summary>Gets the blocks, reading a document that named none as one holding none.</summary>
    public IReadOnlyList<MailBodyBlock> Held => this.Blocks ?? [];

    /// <summary>Gets whether the message is drawn as this document rather than as its plain text.</summary>
    public bool IsDrawn => this.Refusal is MailBodyRefusal.None && this.Held.Count > 0;

    /// <summary>Gets whether the message asked to load something from somebody else's server and was not allowed to.</summary>
    public bool WithheldRemoteContent => this.RemovedRemoteReferenceCount > 0;
}
