// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.SyntheticMail.Generation;

namespace MailFathom.SyntheticMail.UnitTests;

/// <summary>Renders a generated message as one labelled string, so two corpora can be compared field by field.</summary>
/// <remarks>
/// Record equality is not enough here. <see cref="SyntheticEmail" /> holds collections, and a positional record
/// compares those by reference, so two corpora agreeing on every value would still be unequal. Rendering everything —
/// the ancestry, both body alternatives, and the attachment's actual bytes — is what makes "the same seed produces the
/// same messages" an assertion rather than a hope, and a failure then names the field that moved.
/// </remarks>
internal static class CorpusFingerprint
{
    /// <summary>Renders one message.</summary>
    /// <param name="email">The generated message.</param>
    /// <returns>Every value it carries, in one line.</returns>
    internal static string Of(SyntheticEmail email)
    {
        var attachment = email.Attachment is { } carried
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{carried.FileName}/{carried.MediaType}/{carried.MediaSubtype}/{carried.Length}/{Convert.ToHexString(carried.MaterializeContent().Span)}")
            : "-";

        var carbonCopies = string.Join(
            ',',
            email.CarbonCopies.Select(participant => $"{participant.DisplayName} <{participant.Address}>"));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"""
             id={email.MessageId}
             in-reply-to={email.InReplyTo ?? "-"}
             references={string.Join(',', email.References)}
             author={email.Author.DisplayName} <{email.Author.Address}>
             cc={carbonCopies}
             subject={email.Subject}
             sent-at={email.SentAt.ToString("O", CultureInfo.InvariantCulture)}
             shape={email.Body.Shape}
             charset={email.Body.CharacterSet}
             text={email.Body.PlainText}
             html={email.Body.Html}
             attachment={attachment}
             """);
    }

    /// <summary>Renders a whole corpus.</summary>
    /// <param name="corpus">The generated messages.</param>
    /// <returns>One fingerprint per message, in order.</returns>
    internal static IReadOnlyList<string> Of(IEnumerable<SyntheticEmail> corpus) => [.. corpus.Select(Of)];
}
