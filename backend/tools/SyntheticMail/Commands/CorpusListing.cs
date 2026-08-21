// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.SyntheticMail.Generation;

namespace MailFathom.SyntheticMail.Commands;

/// <summary>Writes one generated message as one line, which is what a dry run produces.</summary>
/// <remarks>
/// <para>
/// The line carries every axis the generator varies, so two runs of one seed are compared with an ordinary
/// <c>diff</c> and a difference points at the field that moved rather than at "the mail is different". Nothing about
/// the run itself appears here, which is why it can be compared at all.
/// </para>
/// <para>
/// The fabricated sensitive material a message carries appears as its category and never as its value, which is the
/// rule a finding follows for the reason a finding follows it: a listing that printed the credential would put it in
/// a terminal, a scrollback, and whatever the developer pasted the output into. The seed is what reproduces the
/// value, and the message itself is where it is. The placement is printed beside the category, because a scanner that
/// finds a category everywhere except at the end of a sentence and one that never finds it at all read identically
/// otherwise.
/// </para>
/// </remarks>
internal static class CorpusListing
{
    /// <summary>Describes one message.</summary>
    /// <param name="email">The generated message.</param>
    /// <returns>The line.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="email" /> is <see langword="null" />.</exception>
    internal static string Describe(SyntheticEmail email)
    {
        ArgumentNullException.ThrowIfNull(email);

        var attachment = email.Attachment is { } carried
            ? string.Create(CultureInfo.InvariantCulture, $"{carried.FileName} ({carried.Length} bytes)")
            : "none";

        var sensitive = email.Body.Decoy is { } planted
            ? string.Create(CultureInfo.InvariantCulture, $"{planted.Kind.Label}@{planted.Placement}")
            : "none";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{email.SentAt:yyyy-MM-dd'T'HH:mm:ssK} | <{email.MessageId}> | in-reply-to={email.InReplyTo ?? "-"} | {email.Body.Shape} | {email.Body.CharacterSet} | from={email.Author.Address} | cc={email.CarbonCopies.Count} | attachment={attachment} | sensitive={sensitive} | {email.Subject}");
    }
}
