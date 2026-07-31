// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace MailFathom.Application.Emails;

/// <summary>States which part of a message the searchable text came from, or why the message yielded none.</summary>
/// <remarks>
/// The absent cases stay distinct because they answer different questions about a message that returns no search hit
/// on its body. A message nobody encrypted and that genuinely carried no words is a complete record; an encrypted one
/// is a body that exists and cannot be read here, and merging the two would turn a known gap into a silent one.
/// </remarks>
public enum ExtractedEmailTextSource
{
    /// <summary>The text is what a genuine <c>text/plain</c> body part carried, so nothing was inferred from markup.</summary>
    PlainTextBodyPart = 0,

    /// <summary>The text was derived from an HTML body because the message offered no plain-text alternative, which is lossy.</summary>
    DerivedFromHtmlBodyPart = 1,

    /// <summary>The message carried no readable textual body at all.</summary>
    NoTextualBodyPart = 2,

    /// <summary>The body arrived inside a cryptographic envelope, so no text could be read from it here.</summary>
    EncryptedBody = 3,

    /// <summary>Extraction never ran for this message, so its body has contributed nothing either way.</summary>
    /// <remarks>
    /// This is the state of a message whose raw MIME was never stored because it exceeded the size limit, and of one
    /// whose stored MIME no reader could parse. It is never the source of an <see cref="ExtractedEmailText" />, which
    /// only ever describes a message something did read; it exists so such a message is still indexed on what its
    /// envelope reported instead of being findable by nothing at all.
    /// </remarks>
    BodyNotExtracted = 4,
}

/// <summary>Carries the searchable text derived from one message's body, or the reason there is none.</summary>
/// <remarks>
/// <para>
/// Both texts are present together or absent together: a source that yielded words supplies each of them, and the two
/// absent sources supply neither. The pair is not an optional refinement of one value — <see cref="OriginalText" /> is
/// what the body said and <see cref="TrimmedText" /> is what is worth indexing, and the first is retained because
/// trimming removes quoted history and signatures by heuristic and an over-aggressive cut must never be the only
/// surviving reading of a message.
/// </para>
/// <para>
/// Extracted text is mail content and personal data by default. Nothing here may be written to a log or included in an
/// error message; only <see cref="Source" /> and the character counts are safe to report.
/// </para>
/// </remarks>
public sealed record ExtractedEmailText
{
    private ExtractedEmailText(ExtractedEmailTextSource source, string? originalText, string? trimmedText)
    {
        this.Source = source;
        this.OriginalText = originalText;
        this.TrimmedText = trimmedText;
    }

    /// <summary>Gets the text of a message whose body carried no words.</summary>
    public static ExtractedEmailText NoTextualBody { get; } =
        new(ExtractedEmailTextSource.NoTextualBodyPart, originalText: null, trimmedText: null);

    /// <summary>Gets the text of a message whose body arrived encrypted and is therefore unreadable here.</summary>
    public static ExtractedEmailText EncryptedBody { get; } =
        new(ExtractedEmailTextSource.EncryptedBody, originalText: null, trimmedText: null);

    /// <summary>Gets where the text came from, or why none exists.</summary>
    public ExtractedEmailTextSource Source { get; }

    /// <summary>Gets the extracted body text before quoted history and signatures were removed, or <see langword="null" /> when the message yielded none.</summary>
    public string? OriginalText { get; }

    /// <summary>Gets the body text that remained after trimming, which is the form the lexical index covers.</summary>
    public string? TrimmedText { get; }

    /// <summary>Gets whether any text was extracted at all.</summary>
    public bool HasText => this.TrimmedText is not null;

    /// <summary>Gets whether the text was inferred from markup rather than read from a plain-text part.</summary>
    /// <remarks>
    /// The marker travels with the text so a later chunking or ranking design can decide how much to trust it, instead
    /// of having to re-derive from the message which of the two paths produced the words it is holding.
    /// </remarks>
    public bool IsDerivedFromHtml => this.Source == ExtractedEmailTextSource.DerivedFromHtmlBodyPart;

    /// <summary>Reports text a <c>text/plain</c> body part carried.</summary>
    /// <param name="originalText">The extracted text before trimming.</param>
    /// <param name="trimmedText">The text that remained after quoted history and signatures were removed.</param>
    /// <returns>The extracted text.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="originalText" /> or <paramref name="trimmedText" /> is <see langword="null" />.</exception>
    public static ExtractedEmailText FromPlainTextBody(string originalText, string trimmedText)
    {
        ArgumentNullException.ThrowIfNull(originalText);
        ArgumentNullException.ThrowIfNull(trimmedText);

        return new ExtractedEmailText(ExtractedEmailTextSource.PlainTextBodyPart, originalText, trimmedText);
    }

    /// <summary>Reports text derived from an HTML body, which is a lossy reading of what the message displayed.</summary>
    /// <param name="originalText">The derived text before trimming.</param>
    /// <param name="trimmedText">The derived text that remained after quoted history and signatures were removed.</param>
    /// <returns>The extracted text, marked as a lossy derivation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="originalText" /> or <paramref name="trimmedText" /> is <see langword="null" />.</exception>
    public static ExtractedEmailText DerivedFromHtmlBody(string originalText, string trimmedText)
    {
        ArgumentNullException.ThrowIfNull(originalText);
        ArgumentNullException.ThrowIfNull(trimmedText);

        return new ExtractedEmailText(ExtractedEmailTextSource.DerivedFromHtmlBodyPart, originalText, trimmedText);
    }
}
