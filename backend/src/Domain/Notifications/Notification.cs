// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Domain.Notifications;

/// <summary>States one thing that happened to a person while nobody was looking at the screen.</summary>
/// <remarks>
/// <para>
/// It belongs to the deployment rather than to a device, because the read state has to be the same in both heads and on
/// a second machine, and because most of what produces one is visible only to the service. It is per owner, on the
/// <c>(owner, identifier)</c> axis every account reference already uses.
/// </para>
/// <para>
/// It is a pointer plus the least it takes to draw a row, and never a second copy of the mailbox. The title and the
/// body are derived when the notification is produced rather than by re-reading mail when it is displayed, which is
/// what keeps the notification centre readable without opening anything — and what makes the record derived personal
/// data with the same classification as the mail behind it. Nothing here holds a mail body, an attachment, an address,
/// or credential material.
/// </para>
/// <para>
/// Being derived personal data decides the rest: a notification pointing at a message is erased with that message
/// rather than swept for separately, it falls under the same erasure and export path as the mail it derives from, and
/// it has a retention bound of its own so the table cannot become a mailbox history in miniature.
/// </para>
/// </remarks>
public sealed record Notification
{
    /// <summary>The longest title stored, which is a headline rather than a sentence.</summary>
    public const int MaximumTitleLength = 200;

    /// <summary>The longest body stored, which is the row's second line rather than a message.</summary>
    public const int MaximumBodyLength = 1000;

    /// <summary>The longest source stored, which is generous for an account identifier.</summary>
    public const int MaximumSourceLength = 128;

    private Notification(
        NotificationId id,
        MailOwnerId owner,
        NotificationKind kind,
        string title,
        string body,
        string? source,
        NotificationTarget target,
        NotificationDeduplicationKey deduplicationKey,
        DateTimeOffset occurredAt,
        bool isRead)
    {
        this.Id = id;
        this.Owner = owner;
        this.Kind = kind;
        this.Title = title;
        this.Body = body;
        this.Source = source;
        this.Target = target;
        this.DeduplicationKey = deduplicationKey;
        this.OccurredAt = occurredAt;
        this.IsRead = isRead;
    }

    /// <summary>Gets what addresses this notification.</summary>
    public NotificationId Id { get; }

    /// <summary>Gets the owner it happened to.</summary>
    public MailOwnerId Owner { get; }

    /// <summary>Gets what part of MailFathom it is about.</summary>
    public NotificationKind Kind { get; }

    /// <summary>Gets the headline the row is drawn with.</summary>
    public string Title { get; }

    /// <summary>Gets the second line the row is drawn with.</summary>
    public string Body { get; }

    /// <summary>Gets what the source line names beyond the kind, and <see langword="null" /> where the kind is the whole of it.</summary>
    /// <remarks>
    /// It is MailFathom's own name for where the notification came from — an account identifier, today — rather than
    /// anything read from mail, and the client composes the displayed line from the kind and this together.
    /// </remarks>
    public string? Source { get; }

    /// <summary>Gets where opening it leads.</summary>
    public NotificationTarget Target { get; }

    /// <summary>Gets the condition it was raised for, which is what stops it being said twice while it is unread.</summary>
    public NotificationDeduplicationKey DeduplicationKey { get; }

    /// <summary>Gets when the thing it describes happened.</summary>
    public DateTimeOffset OccurredAt { get; }

    /// <summary>Gets whether the person has read it.</summary>
    public bool IsRead { get; }

    /// <summary>Composes a notification that has not been read.</summary>
    /// <param name="id">What addresses the notification.</param>
    /// <param name="owner">The owner it happened to.</param>
    /// <param name="kind">What part of MailFathom it is about.</param>
    /// <param name="title">The headline the row is drawn with.</param>
    /// <param name="body">The second line the row is drawn with.</param>
    /// <param name="source">What the source line names beyond the kind, or <see langword="null" /> where the kind is the whole of it.</param>
    /// <param name="target">Where opening it leads.</param>
    /// <param name="deduplicationKey">The condition it was raised for.</param>
    /// <param name="occurredAt">When the thing it describes happened.</param>
    /// <returns>An unread notification.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="title" /> or <paramref name="body" /> is blank, when <paramref name="source" /> is present and blank, when <paramref name="owner" /> names nobody, or when <paramref name="id" /> or <paramref name="deduplicationKey" /> is the struct default.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="kind" /> is not a declared kind, or when a text exceeds the bound stated for it.</exception>
    /// <remarks>
    /// The owner has to be a named one, because a row written under the unspecified identity would belong to nobody:
    /// unreachable by any read and uncollected by any erasure.
    /// </remarks>
    public static Notification Compose(
        NotificationId id,
        MailOwnerId owner,
        NotificationKind kind,
        string title,
        string body,
        string? source,
        NotificationTarget target,
        NotificationDeduplicationKey deduplicationKey,
        DateTimeOffset occurredAt)
    {
        Validate(owner, kind, id, deduplicationKey, target);

        return new Notification(
            id,
            owner,
            kind,
            Bounded(title, MaximumTitleLength, nameof(title)),
            Bounded(body, MaximumBodyLength, nameof(body)),
            source is null ? null : Bounded(source, MaximumSourceLength, nameof(source)),
            target,
            deduplicationKey,
            occurredAt,
            isRead: false);
    }

    /// <summary>Restores a notification this deployment already kept, with the read state it was stored under.</summary>
    /// <param name="id">What addresses the notification.</param>
    /// <param name="owner">The owner it happened to.</param>
    /// <param name="kind">What part of MailFathom it is about.</param>
    /// <param name="title">The headline the row is drawn with.</param>
    /// <param name="body">The second line the row is drawn with.</param>
    /// <param name="source">What the source line names beyond the kind, or <see langword="null" /> where the kind is the whole of it.</param>
    /// <param name="target">Where opening it leads.</param>
    /// <param name="deduplicationKey">The condition it was raised for.</param>
    /// <param name="occurredAt">When the thing it describes happened.</param>
    /// <param name="isRead">Whether the person has read it.</param>
    /// <returns>The notification as it stands.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="title" /> or <paramref name="body" /> is blank, when <paramref name="source" /> is present and blank, when <paramref name="owner" /> names nobody, or when <paramref name="id" /> or <paramref name="deduplicationKey" /> is the struct default.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="kind" /> is not a declared kind, or when a text exceeds the bound stated for it.</exception>
    /// <remarks>
    /// It validates exactly what <see cref="Compose" /> validates rather than trusting the store, because a row read
    /// back is input from outside this process however it got there: a hand-edited table and a migration that widened
    /// a column reach a reader the same way a producer does. The one thing it takes that composing does not is the
    /// read state, which is the store's to say and never a producer's.
    /// </remarks>
    public static Notification Restore(
        NotificationId id,
        MailOwnerId owner,
        NotificationKind kind,
        string title,
        string body,
        string? source,
        NotificationTarget target,
        NotificationDeduplicationKey deduplicationKey,
        DateTimeOffset occurredAt,
        bool isRead)
    {
        Validate(owner, kind, id, deduplicationKey, target);

        return new Notification(
            id,
            owner,
            kind,
            Bounded(title, MaximumTitleLength, nameof(title)),
            Bounded(body, MaximumBodyLength, nameof(body)),
            source is null ? null : Bounded(source, MaximumSourceLength, nameof(source)),
            target,
            deduplicationKey,
            occurredAt,
            isRead);
    }

    /// <summary>Refuses the identities and the kind that no notification can be built from, whether composed or restored.</summary>
    private static void Validate(
        MailOwnerId owner,
        NotificationKind kind,
        NotificationId id,
        NotificationDeduplicationKey deduplicationKey,
        NotificationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!owner.IsSpecified)
        {
            throw new ArgumentException(
                "A notification happens to a named owner, so it is never raised under the unspecified one.",
                nameof(owner));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "A notification carries a declared kind.");
        }

        if (!id.IsSpecified)
        {
            throw new ArgumentException("A notification is addressed by a specified identifier.", nameof(id));
        }

        if (!deduplicationKey.IsSpecified)
        {
            throw new ArgumentException(
                "A notification names the condition it was raised for, so the deduplication rule has something to hold.",
                nameof(deduplicationKey));
        }
    }

    private static string Bounded(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        var trimmed = value.Trim();

        ArgumentOutOfRangeException.ThrowIfGreaterThan(trimmed.Length, maximumLength, parameterName);

        return trimmed;
    }
}
