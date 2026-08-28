// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Mail;
using Microsoft.Extensions.Localization;

namespace MailFathom.Client.Presentation.Spaces.Mail.Reading;

/// <summary>One message's body as the reading pane draws it, with every sentence about it already in words.</summary>
/// <remarks>
/// <para>
/// The pane draws this rather than the deployment's answer, which is what keeps the decisions out of the view: whether
/// the document is drawn at all, why it is not, and what a reader is told about what was left out are taken once, off
/// the visual tree, where a test reads them directly.
/// </para>
/// <para>
/// Everything it carries is mail, so none of it is logged, written to local storage, or put in a telemetry event, and
/// none of it appears in a failure message.
/// </para>
/// </remarks>
public sealed record MailBodyReading
{
    private const string AvailabilityKeyPrefix = "MailBody.Availability.";
    private const string UnrecognizedAvailabilityKey = "MailBody.Availability.Unrecognized";
    private const string ReadableAvailability = "Readable";
    private const string WithheldRemoteKey = "MailBody.Notice.RemoteContent.Message";
    private const string ShownRemoteKey = "MailBody.Notice.RemoteContentShown.Message";
    private const string UndrawnImagesKey = "MailBody.Notice.UndrawnImages.Message";

    private MailBodyReading(MailBodyWords words)
    {
        this.Words = words;
        this.Blocks = [];
        this.PlainText = string.Empty;
        this.Reason = string.Empty;
        this.WithheldRemoteContent = string.Empty;
        this.ShownRemoteContent = string.Empty;
        this.UndrawnImages = string.Empty;
    }

    /// <summary>Gets whether a message is open at all, as against a pane nothing has been opened in.</summary>
    public bool IsOpen { get; private init; }

    /// <summary>Gets the blocks the pane draws, which is empty wherever the message is read as words instead.</summary>
    public IReadOnlyList<MailBodyBlock> Blocks { get; private init; }

    /// <summary>Gets whether the pane draws the document rather than the message's plain text.</summary>
    public bool DrawsDocument { get; private init; }

    /// <summary>Gets the message as words, which is what the pane shows wherever it does not draw the document.</summary>
    public string PlainText { get; private init; }

    /// <summary>Gets whether the pane shows the message as words.</summary>
    public bool ShowsPlainText => this.IsOpen && !this.DrawsDocument;

    /// <summary>Gets why this message is read as words rather than drawn, or nothing where it is drawn.</summary>
    /// <remarks>
    /// Always accompanied by the words themselves rather than shown in their place: a refusal a reader can see the
    /// reason for but not the message is a message they have lost, and the plain text is a rendering in its own right.
    /// </remarks>
    public string Reason { get; private init; }

    /// <summary>Gets whether there is a reason to show.</summary>
    public bool HasReason => this.Reason.Length > 0;

    /// <summary>Gets what the message asked to load from somebody else's server and was not allowed to, or nothing.</summary>
    public string WithheldRemoteContent { get; private init; }

    /// <summary>Gets whether anything was withheld, which is what offers the reader the choice.</summary>
    public bool WithholdsRemoteContent => this.WithheldRemoteContent.Length > 0;

    /// <summary>Gets what this read fetched from somebody else's server because the reader asked, or nothing.</summary>
    public string ShownRemoteContent { get; private init; }

    /// <summary>Gets whether this read was the one the reader asked remote pictures for.</summary>
    public bool ShowsRemoteContent => this.ShownRemoteContent.Length > 0;

    /// <summary>Gets what of the message's own pictures was left undrawn, or nothing.</summary>
    public string UndrawnImages { get; private init; }

    /// <summary>Gets whether any of the message's own pictures were left undrawn.</summary>
    public bool HasUndrawnImages => this.UndrawnImages.Length > 0;

    /// <summary>Gets whether a bound stopped the rendering before the end of the message.</summary>
    public bool WasTruncated { get; private init; }

    /// <summary>Gets whether the deployment wrote a revision of the contract that is newer than this build reads.</summary>
    /// <remarks>
    /// A notice rather than a refusal. A desktop head and a deployment are updated separately, so meeting a newer
    /// revision is ordinary, and each block this build does not know already degrades to a placeholder of its own —
    /// what a reader gains here is being told that what they are looking at may be missing something.
    /// </remarks>
    public bool DeploymentAhead { get; private init; }

    /// <summary>Gets the sentences the drawing itself composes.</summary>
    public MailBodyWords Words { get; private init; }

    /// <summary>Reads a pane with nothing open in it.</summary>
    /// <param name="words">Where the sentences come from.</param>
    /// <returns>The empty reading.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="words" /> is <see langword="null" />.</exception>
    public static MailBodyReading Nothing(IStringLocalizer words)
    {
        ArgumentNullException.ThrowIfNull(words);

        return new MailBodyReading(MailBodyWords.From(words));
    }

    /// <summary>Reads what the deployment answered into what the pane draws.</summary>
    /// <param name="body">The body as the deployment served it.</param>
    /// <param name="words">Where the sentences come from.</param>
    /// <returns>The reading.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body" /> or <paramref name="words" /> is <see langword="null" />.</exception>
    public static MailBodyReading Of(DeploymentMailBody body, IStringLocalizer words)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(words);

        var document = body.Document;
        var drawn = document?.IsDrawn is true;

        return new MailBodyReading(MailBodyWords.From(words))
        {
            IsOpen = true,
            Blocks = drawn ? document!.Held : [],
            DrawsDocument = drawn,
            PlainText = body.PlainText?.Text ?? string.Empty,
            Reason = drawn ? string.Empty : ReasonFor(body, words),
            WithheldRemoteContent = document is { RemovedRemoteReferenceCount: > 0 }
                ? words[WithheldRemoteKey, document.RemovedRemoteReferenceCount]
                : string.Empty,
            ShownRemoteContent = body.RemoteImagesRequested && document is { RetainedRemoteImageCount: > 0 }
                ? words[ShownRemoteKey, document.RetainedRemoteImageCount]
                : string.Empty,
            UndrawnImages = document is { UndrawnInlineImageCount: > 0 }
                ? words[UndrawnImagesKey, document.UndrawnInlineImageCount]
                : string.Empty,
            WasTruncated = body.PlainText?.WasTruncated is true || document?.Truncated is true,
            DeploymentAhead = document?.SchemaVersion > MailBodyDocument.ImplementedSchemaVersion,
        };
    }

    /// <summary>Says why a message is read as words rather than drawn, in the language the person is reading in.</summary>
    /// <remarks>
    /// A body that could not be read at all is answered before the document is looked at, because the document is
    /// absent in exactly those states and its absence says nothing about which of them it was.
    /// </remarks>
    private static string ReasonFor(DeploymentMailBody body, IStringLocalizer words)
    {
        if (!string.Equals(body.Availability, ReadableAvailability, StringComparison.Ordinal))
        {
            var named = words[$"{AvailabilityKeyPrefix}{body.Availability}"];

            return named.ResourceNotFound ? words[UnrecognizedAvailabilityKey] : named.Value;
        }

        var refusal = body.Document?.Refusal ?? MailBodyRefusal.NothingRenderable;

        return words[MailBodyWords.RefusalResourceKeyFor(
            refusal is MailBodyRefusal.None ? MailBodyRefusal.NothingRenderable : refusal)];
    }
}
