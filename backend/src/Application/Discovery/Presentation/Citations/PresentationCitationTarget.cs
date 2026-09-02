// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Discovery.Presentation.Citations;

/// <summary>What one citation resolves to: an email, one passage of an email, or one of its attachments.</summary>
/// <remarks>
/// <para>
/// Three targets and no fourth. The hierarchy is closed by a private protected constructor, so a target outside this
/// file cannot be declared even from another assembly, and every one of the three names something a reader can be taken
/// to. A citation that resolved to a search, a score, or a paraphrase would be a fact resting on the assistant's own
/// working rather than on the correspondence, which is the whole thing a citation exists to rule out.
/// </para>
/// <para>
/// The email is named by its local identity rather than by its remote occurrence, because a citation is followed inside
/// this deployment and an occurrence moves when a folder is renamed or a mailbox is rebuilt.
/// </para>
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(EmailCitationTarget), EmailCitationTarget.Kind)]
[JsonDerivedType(typeof(FragmentCitationTarget), FragmentCitationTarget.Kind)]
[JsonDerivedType(typeof(AttachmentCitationTarget), AttachmentCitationTarget.Kind)]
public abstract record PresentationCitationTarget
{
    private protected PresentationCitationTarget(StoredEmailId email) => this.Email = email;

    /// <summary>Gets the email the citation is followed to, whichever of the three the target is.</summary>
    public StoredEmailId Email { get; }
}

/// <summary>A citation resolving to a whole email.</summary>
/// <remarks>The target for a fact that rests on the message as such — that it was sent, when, and by whom.</remarks>
public sealed record EmailCitationTarget : PresentationCitationTarget
{
    /// <summary>The value the type discriminator carries on the wire.</summary>
    public const string Kind = "email";

    /// <summary>Initializes a citation resolving to a whole email.</summary>
    /// <param name="email">The email the citation is followed to.</param>
    public EmailCitationTarget(StoredEmailId email)
        : base(email)
    {
    }
}

/// <summary>A citation resolving to one persisted passage of an email.</summary>
/// <remarks>
/// The target for a fact taken from a particular part of a long message. It carries the email as well as the passage so
/// a client can open the message even where the passage no longer resolves — a mailbox rebuilt since the run keeps its
/// emails and re-derives its passages.
/// </remarks>
public sealed record FragmentCitationTarget : PresentationCitationTarget
{
    /// <summary>The value the type discriminator carries on the wire.</summary>
    public const string Kind = "fragment";

    /// <summary>Initializes a citation resolving to one passage of an email.</summary>
    /// <param name="email">The email the passage belongs to.</param>
    /// <param name="fragment">The passage the fact was taken from.</param>
    public FragmentCitationTarget(StoredEmailId email, EmailChunkId fragment)
        : base(email) =>
        this.Fragment = fragment;

    /// <summary>Gets the passage the fact was taken from.</summary>
    public EmailChunkId Fragment { get; }
}

/// <summary>A citation resolving to one attachment of an email.</summary>
/// <remarks>
/// The attachment is named by its position in the order the message's structure is walked, which is the same pair the
/// download route is addressed with. A file name is not an identity: one message may carry two attachments named alike,
/// and a name is content the sender chose.
/// </remarks>
public sealed record AttachmentCitationTarget : PresentationCitationTarget
{
    /// <summary>The value the type discriminator carries on the wire.</summary>
    public const string Kind = "attachment";

    /// <summary>Initializes a citation resolving to one attachment of an email.</summary>
    /// <param name="email">The email carrying the attachment.</param>
    /// <param name="attachmentPosition">The attachment's zero-based position in the order the message's structure is walked.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="attachmentPosition" /> is negative.</exception>
    public AttachmentCitationTarget(StoredEmailId email, int attachmentPosition)
        : base(email)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(attachmentPosition);

        this.AttachmentPosition = attachmentPosition;
    }

    /// <summary>Gets the attachment's zero-based position in the order the message's structure is walked.</summary>
    public int AttachmentPosition { get; }
}
