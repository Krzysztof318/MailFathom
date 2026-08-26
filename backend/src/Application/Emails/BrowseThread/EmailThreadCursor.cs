// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Paging;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.BrowseThread;

/// <summary>Marks the message one page of a conversation ended on, so the next page continues after it.</summary>
/// <remarks>
/// <para>
/// The boundary is a message rather than a position, and that is the whole design. A conversation's order is derived on
/// every read from the reply relation and the sent times, so an offset into it names nothing once a reply arrives in the
/// middle — while the message a page ended on is still the same message, wherever the new arrival pushed it. The next
/// page is what follows that message in the order the read just derived.
/// </para>
/// <para>
/// The fingerprint is of the conversation the cursor was issued for, which is what a conversation has instead of the
/// filter set a timeline cursor carries: a thread is read by membership, so the only thing a boundary here can belong to
/// is the thread it was taken in. A cursor presented against a different conversation is refused rather than resolved.
/// </para>
/// <para>
/// The encoded form is <see cref="KeysetCursorPayload" />'s, which every keyset cursor here shares. Its position field
/// is written absent, because the place a message holds in a conversation is not an instant: the sent time settles
/// messages answering the same parent and orders nothing on its own, so a boundary written as one would name a different
/// message than the one the page ended on.
/// </para>
/// </remarks>
public readonly record struct EmailThreadCursor
{
    private EmailThreadCursor(StoredEmailId storedEmailId, string threadFingerprint)
    {
        this.StoredEmailId = storedEmailId;
        this.ThreadFingerprint = threadFingerprint;
    }

    /// <summary>Gets the message the page ended on, which the next page reads beyond.</summary>
    public StoredEmailId StoredEmailId { get; }

    /// <summary>Gets the fingerprint of the conversation this cursor was issued for.</summary>
    public string ThreadFingerprint { get; }

    /// <summary>Reduces one conversation to the fingerprint its cursors carry.</summary>
    /// <param name="threadId">The conversation, as the request naming it wrote the identifier.</param>
    /// <returns>The fingerprint.</returns>
    public static string FingerprintOf(EmailThreadId threadId) =>
        PageFilterFingerprint.Of(threadId.Value.ToString("N"));

    /// <summary>Creates the cursor that continues a conversation after one message.</summary>
    /// <param name="storedEmailId">The message the page ended on.</param>
    /// <param name="threadFingerprint">The fingerprint of the conversation the page was read in.</param>
    /// <returns>The cursor.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="threadFingerprint" /> is blank.</exception>
    public static EmailThreadCursor After(StoredEmailId storedEmailId, string threadFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadFingerprint);

        return new EmailThreadCursor(storedEmailId, threadFingerprint);
    }

    /// <summary>Reads a cursor a caller presented.</summary>
    /// <param name="text">The encoded cursor, as a previous page returned it.</param>
    /// <param name="cursor">The decoded cursor when the text is one this version issued; otherwise the struct default.</param>
    /// <returns><see langword="true" /> when the text decoded into a usable cursor; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// A payload carrying an instant is refused, because this reading writes none and one that arrived carrying one was
    /// built rather than issued. Whether a decoded cursor belongs to the conversation in hand is a separate question its
    /// <see cref="ThreadFingerprint" /> answers, and one this method deliberately does not ask.
    /// </remarks>
    public static bool TryDecode(string? text, out EmailThreadCursor cursor)
    {
        cursor = default;

        if (!KeysetCursorPayload.TryDecode(text, out var payload) || payload.Position is not null)
        {
            return false;
        }

        cursor = new EmailThreadCursor(StoredEmailId.Create(payload.Identity), payload.FilterFingerprint);

        return true;
    }

    /// <summary>Writes the cursor as the opaque string a caller presents to continue the conversation.</summary>
    /// <returns>The encoded cursor.</returns>
    public string Encode() => KeysetCursorPayload
        .At(position: null, this.StoredEmailId.Value, this.ThreadFingerprint)
        .Encode();
}
