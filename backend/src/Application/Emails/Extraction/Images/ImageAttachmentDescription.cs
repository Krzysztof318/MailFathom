// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Extraction.Images;

/// <summary>What describing one image attachment produced: the words, or the reason there are none.</summary>
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
    private ImageAttachmentDescription(string? text, ImageDescriptionRefusal? refusal)
    {
        this.Text = text;
        this.Refusal = refusal;
    }

    /// <summary>Gets what the picture shows, or <see langword="null" /> where nothing was described.</summary>
    public string? Text { get; }

    /// <summary>Gets why nothing was described, or <see langword="null" /> where something was.</summary>
    public ImageDescriptionRefusal? Refusal { get; }

    /// <summary>Carries what the model said the picture shows.</summary>
    /// <param name="text">The description, which is never blank.</param>
    /// <returns>The described result.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="text" /> is <see langword="null" />, empty, or whitespace.</exception>
    public static ImageAttachmentDescription Described(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        return new ImageAttachmentDescription(text, refusal: null);
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

        return new ImageAttachmentDescription(text: null, refusal);
    }
}
