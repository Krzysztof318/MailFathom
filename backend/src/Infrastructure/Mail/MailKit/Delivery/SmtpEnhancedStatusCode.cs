// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Infrastructure.Mail.MailKit.Delivery;

/// <summary>One RFC 3463 enhanced mail system status code, as the three numbers it is made of.</summary>
/// <param name="Class">The broad outcome: 2 success, 4 persistent transient failure, 5 permanent failure.</param>
/// <param name="Subject">What the outcome is about, such as the addressing, the mailbox, or the security of the exchange.</param>
/// <param name="Detail">The refinement of the subject.</param>
/// <remarks>
/// It is parsed out of a server's reply text and carries the three numbers alone. The rest of that text is a sentence
/// the server wrote and routinely names the recipient it is about, so it is read here and never kept — which is what
/// lets a classification be recorded and logged while a reply naming a person's address is not.
/// </remarks>
internal sealed record SmtpEnhancedStatusCode(int Class, int Subject, int Detail)
{
    /// <summary>The classes RFC 3463 defines; anything else in that position is not one of these codes.</summary>
    private static readonly int[] DefinedClasses = [2, 4, 5];

    /// <summary>The largest value the subject and detail parts may carry, which RFC 3463 bounds at three digits.</summary>
    private const int LargestPart = 999;

    /// <summary>Reads the enhanced status code a server put at the front of its reply, when it put one there.</summary>
    /// <param name="replyText">The reply text as the server sent it.</param>
    /// <param name="enhancedStatusCode">The parsed code when the reply opens with one; otherwise <see langword="null" />.</param>
    /// <returns><see langword="true" /> when the reply opens with a well-formed code.</returns>
    /// <remarks>
    /// RFC 3463 puts the code first in the reply text and separates it from the human-readable part with a space, so
    /// only the first token of the first line is examined. A server that advertises no enhanced status codes, or one
    /// that opens with prose, simply has none here — which is an ordinary answer rather than a failure to report.
    /// </remarks>
    internal static bool TryParse(string? replyText, [NotNullWhen(true)] out SmtpEnhancedStatusCode? enhancedStatusCode)
    {
        enhancedStatusCode = null;

        if (string.IsNullOrWhiteSpace(replyText))
        {
            return false;
        }

        var firstToken = ReadFirstToken(replyText);

        var classSeparator = firstToken.IndexOf('.');
        if (classSeparator < 0)
        {
            return false;
        }

        var subjectSeparator = firstToken[(classSeparator + 1)..].IndexOf('.');
        if (subjectSeparator < 0)
        {
            return false;
        }

        if (!TryReadPart(firstToken[..classSeparator], out var statusClass)
            || !TryReadPart(firstToken.Slice(classSeparator + 1, subjectSeparator), out var subject)
            || !TryReadPart(firstToken[(classSeparator + subjectSeparator + 2)..], out var detail))
        {
            return false;
        }

        if (!DefinedClasses.Contains(statusClass))
        {
            return false;
        }

        enhancedStatusCode = new SmtpEnhancedStatusCode(statusClass, subject, detail);

        return true;
    }

    /// <summary>Renders the code the way RFC 3463 writes it, which is what a log line and a record carry.</summary>
    /// <returns>The three parts separated by dots.</returns>
    public override string ToString() => $"{this.Class}.{this.Subject}.{this.Detail}";

    /// <summary>Takes the first whitespace-delimited token of the first line, which is where the code sits when there is one.</summary>
    private static ReadOnlySpan<char> ReadFirstToken(string replyText)
    {
        var firstLine = replyText.AsSpan();

        var lineEnd = firstLine.IndexOfAny('\r', '\n');
        if (lineEnd >= 0)
        {
            firstLine = firstLine[..lineEnd];
        }

        firstLine = firstLine.TrimStart();

        var tokenEnd = firstLine.IndexOfAny(' ', '\t');

        return tokenEnd >= 0 ? firstLine[..tokenEnd] : firstLine;
    }

    /// <summary>Reads one part of the code, refusing anything a digit sequence within the RFC's bound is not.</summary>
    private static bool TryReadPart(ReadOnlySpan<char> part, out int value)
    {
        value = 0;

        if (part.IsEmpty || part.Length > 3 || part.ContainsAnyExceptInRange('0', '9'))
        {
            return false;
        }

        return int.TryParse(part, out value) && value <= LargestPart;
    }
}
