// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Extraction.Images;

/// <summary>What reading one image attachment produced: a transcription of its words, a description of what it shows, or the reason there are neither.</summary>
/// <remarks>
/// <para>
/// Exactly one of the two is present, and the two factories below are the only way to make one — which is why the
/// constructor is private rather than the positional one a record would otherwise publish alongside a <c>with</c>
/// expression. Both would let a caller compose a value carrying neither, and a caller branching on the contract stated
/// here would then store nothing while recording no reason. A refusal is a result
/// rather than an exception because every one of them is an ordinary property of the mail a mailbox holds — a
/// signature image in a format nothing reads, a photograph larger than one request sends, a provider having a bad
/// afternoon — and none of them is a fault in this deployment worth unwinding a background run for.
/// </para>
/// <para>
/// The text is a machine's account of octets a stranger composed, so it is untrusted twice over: whoever stores,
/// indexes, presents, or hands it to a model treats it exactly as they treat text extracted from an attachment, and
/// never as something the sender wrote.
/// </para>
/// </remarks>
public sealed record ImageAttachmentDescription
{
    private ImageAttachmentDescription(string? text, bool isTranscription, ImageDescriptionRefusal? refusal)
    {
        this.Text = text;
        this.IsTranscription = isTranscription;
        this.Refusal = refusal;
    }

    /// <summary>Gets what the picture shows, or <see langword="null" /> where nothing was described.</summary>
    public string? Text { get; }

    /// <summary>Gets whether <see cref="Text" /> is the words the picture carries rather than an account of what it shows.</summary>
    /// <remarks>
    /// The model decides this, and the answer decides where the words are searched: a transcription is what somebody
    /// wrote, so it is searched as written text, while a description is a machine's sentence about a picture and ranks
    /// below everything written. Only a result the model explicitly marked as a transcription is one, so an answer that
    /// ignored the format stays in the lower-ranked place.
    /// </remarks>
    public bool IsTranscription { get; }

    /// <summary>Gets why nothing was described, or <see langword="null" /> where something was.</summary>
    public ImageDescriptionRefusal? Refusal { get; }

    /// <summary>Gets whether a request actually left this deployment for the provider.</summary>
    /// <remarks>
    /// What a caller counting provider calls charges against, and the reason it is answered here rather than by that
    /// caller: which refusals are reached before a request is composed is a property of the describing port, and a
    /// counter that re-derived it would charge a deployment for the pictures it declined to send. Six of the nine
    /// refusals are settled from the switch, the format, or the header, so the call happens exactly where words came
    /// back or where the provider itself answered.
    /// </remarks>
    public bool ReachedProvider => this.Refusal is null
        or ImageDescriptionRefusal.ProviderTimedOut
        or ImageDescriptionRefusal.ProviderUnavailable
        or ImageDescriptionRefusal.ProviderRefused;

    /// <summary>Carries what the model said the picture shows.</summary>
    /// <param name="text">The description, which is never blank.</param>
    /// <returns>The described result.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="text" /> is <see langword="null" />, empty, or whitespace.</exception>
    public static ImageAttachmentDescription Described(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        return new ImageAttachmentDescription(text, isTranscription: false, refusal: null);
    }

    /// <summary>Carries the words the model read off a picture of a document.</summary>
    /// <param name="text">The transcription, which is never blank.</param>
    /// <returns>The transcribed result.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="text" /> is <see langword="null" />, empty, or whitespace.</exception>
    public static ImageAttachmentDescription Transcribed(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        return new ImageAttachmentDescription(text, isTranscription: true, refusal: null);
    }

    /// <summary>Records why the attachment produced no description.</summary>
    /// <param name="refusal">What stopped it.</param>
    /// <returns>The refused result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="refusal" /> names no declared reason.</exception>
    public static ImageAttachmentDescription Refused(ImageDescriptionRefusal refusal)
    {
        if (!Enum.IsDefined(refusal))
        {
            throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "The refusal names no declared reason.");
        }

        return new ImageAttachmentDescription(text: null, isTranscription: false, refusal);
    }
}
