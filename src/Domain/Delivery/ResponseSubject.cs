// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.RegularExpressions;

namespace MailFathom.Domain.Delivery;

/// <summary>Writes the subject a reply or a forward carries, from the subject the answered message carried.</summary>
/// <remarks>
/// <para>
/// The convention is one prefix and one only. A client that adds its own without recognizing the one already there
/// produces <c>Re: Re: Re:</c>, which is how a thread ends up unreadable in every participant's list after four
/// exchanges — and recognizing only the English prefix produces exactly that against every correspondent whose client
/// is not in English.
/// </para>
/// <para>
/// So the comparison admits the prefixes actually in use rather than the one this system writes: <c>Aw</c> from German
/// clients, <c>Sv</c> and <c>Svar</c> from Scandinavian ones, <c>Vs</c> and <c>Vl</c> from Finnish, <c>Odp</c> from
/// Polish, <c>Res</c> from Portuguese, <c>Rif</c> from Italian, <c>Ynt</c> from Turkish, and the Chinese forms among
/// them. What a missing entry costs is a doubled prefix rather than a broken thread, which is why the set is a
/// judgement about what is common rather than an attempt at every locale that exists.
/// </para>
/// <para>
/// The numbered form some clients write — <c>Re[2]:</c> — is recognized as a prefix as well, and is left exactly as it
/// was written rather than incremented. Counting exchanges is one client's convention and no part of the standard.
/// </para>
/// </remarks>
public static partial class ResponseSubject
{
    /// <summary>The prefix this system writes on a reply.</summary>
    private const string ReplyMarker = "Re";

    /// <summary>The prefix this system writes on a forward.</summary>
    private const string ForwardMarker = "Fwd";

    /// <summary>Writes the subject of a reply to the given subject.</summary>
    /// <param name="answeredSubject">The subject the answered message carried, or <see langword="null" /> when it carried none.</param>
    /// <returns>The subject the reply carries.</returns>
    public static string ForReply(string? answeredSubject) =>
        Prefixed(ReplyMarker, ReplyPrefix(), answeredSubject);

    /// <summary>Writes the subject of a forward of the given subject.</summary>
    /// <param name="forwardedSubject">The subject the forwarded message carried, or <see langword="null" /> when it carried none.</param>
    /// <returns>The subject the forward carries.</returns>
    public static string ForForward(string? forwardedSubject) =>
        Prefixed(ForwardMarker, ForwardPrefix(), forwardedSubject);

    /// <summary>Adds the prefix, unless the subject already opens with one the ecosystem writes.</summary>
    /// <remarks>
    /// The subject arrives from a stored message, so it is decoded mail content rather than an authored value: a
    /// control character surviving an encoded word would end the composed header early, and the composition refuses one
    /// rather than repairing it. Removing it here is what keeps that refusal about what an author wrote — nobody can
    /// correct a subject somebody else sent them, and a message whose subject carried one would otherwise be
    /// unanswerable.
    /// </remarks>
    private static string Prefixed(string marker, Regex existingPrefix, string? subject)
    {
        var readable = Readable(subject);

        if (existingPrefix.IsMatch(readable))
        {
            return readable;
        }

        return readable.Length == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{marker}:")
            : string.Create(CultureInfo.InvariantCulture, $"{marker}: {readable}");
    }

    /// <summary>Reduces a stored subject to what a composed header can carry.</summary>
    private static string Readable(string? subject) =>
        string.IsNullOrWhiteSpace(subject)
            ? string.Empty
            : new string([.. subject.Where(static character => !char.IsControl(character))]).Trim();

    /// <summary>Matches the reply prefixes clients in common use write.</summary>
    [GeneratedRegex(
        @"^(?:re|aw|antw(?:ort)?|sv|svar|vs|vl|odp|res|ref|rif|ynt|回复|回覆|答复)[ \t]*(?:[\[(]\d{1,3}[\])])?[ \t]*:",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReplyPrefix();

    /// <summary>Matches the forward prefixes clients in common use write.</summary>
    [GeneratedRegex(
        @"^(?:fwd?|wg|tr|doorst|enc|rv|vb|vl|pd|转发|轉寄|轉發)[ \t]*(?:[\[(]\d{1,3}[\])])?[ \t]*:",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ForwardPrefix();
}
