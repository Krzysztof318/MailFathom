// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace MailFathom.Infrastructure.Spam;

/// <summary>One answer the spam daemon wrote, split into the three parts its protocol defines.</summary>
/// <remarks>
/// <para>
/// The protocol is HTTP-shaped without being HTTP: a status line naming the protocol version and a numeric code, a
/// header block, a blank line, and a body whose meaning depends on the command that was sent. Nothing here interprets
/// the body — a symbol list and a header block are both just text at this level — so one parser serves every command the
/// adapter issues.
/// </para>
/// <para>
/// Everything the daemon sends is treated as untrusted input. It is a separate process reached over a socket, and an
/// address an operator mistyped reaches whatever else is listening on that port, so a reply that does not parse is a
/// reply that produced nothing rather than an exception from somewhere inside a split.
/// </para>
/// </remarks>
internal sealed record SpamdReply
{
    /// <summary>The status line's fixed prefix, which is how a reply is recognised as this protocol's at all.</summary>
    private const string StatusLinePrefix = "SPAMD/";

    /// <summary>The code the daemon reports when it answered the command rather than refusing it.</summary>
    /// <remarks>The codes are <c>sysexits.h</c>'s, where zero is success and every other value closes the connection after the status line.</remarks>
    private const string SuccessCode = "0";

    private const string HeaderBlockSeparator = "\r\n\r\n";

    private SpamdReply(string protocolVersion, IReadOnlyDictionary<string, string> headers, string body)
    {
        this.ProtocolVersion = protocolVersion;
        this.Headers = headers;
        this.Body = body;
    }

    /// <summary>Gets the protocol version the daemon answered under, such as <c>1.1</c>.</summary>
    /// <remarks>
    /// The version of the conversation rather than of the rule corpus, which the protocol carries nowhere. It is what a
    /// corpus identity falls back to when the daemon's own configuration removed the header that names its release.
    /// </remarks>
    public string ProtocolVersion { get; }

    /// <summary>Gets the reply's headers, keyed case-insensitively as the protocol treats them.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>Gets whatever followed the blank line, which is empty for a command that answers with headers alone.</summary>
    public string Body { get; }

    /// <summary>Reads one answer, or reports that the bytes are not one.</summary>
    /// <param name="answer">Everything the daemon wrote before it closed its side.</param>
    /// <param name="reply">The parsed answer, when the bytes were one.</param>
    /// <returns><see langword="true" /> when the bytes are a successful answer in this protocol.</returns>
    /// <remarks>
    /// A refusal — any non-zero code — is reported as a failure to parse rather than as a reply with a code on it,
    /// because no caller here acts differently on which refusal it was: the daemon closes the connection after the
    /// status line, so there is nothing else to read either way.
    /// </remarks>
    public static bool TryParse(ReadOnlySpan<byte> answer, [NotNullWhen(true)] out SpamdReply? reply)
    {
        reply = null;

        // The daemon writes ASCII in the status line and the headers, and the body of the commands issued here is a
        // rule list or a header block. Latin-1 decodes every byte to exactly one character and never throws, which
        // keeps a malformed reply a parse failure rather than a decoding exception.
        var text = Encoding.Latin1.GetString(answer);
        var separator = text.IndexOf(HeaderBlockSeparator, StringComparison.Ordinal);
        var head = separator < 0 ? text : text[..separator];
        var body = separator < 0 ? string.Empty : text[(separator + HeaderBlockSeparator.Length)..];

        var lines = head.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length is 0 || !TryReadStatusLine(lines[0], out var protocolVersion))
        {
            return false;
        }

        reply = new SpamdReply(protocolVersion, ReadHeaders(lines.Skip(1)), body);

        return true;
    }

    /// <summary>Reads the verdict, the score, and the threshold the daemon judged the message by.</summary>
    /// <param name="score">The score the corpus assigned.</param>
    /// <param name="threshold">The score at or above which the daemon calls a message spam.</param>
    /// <returns><see langword="true" /> when the reply carries a usable pair of numbers.</returns>
    /// <remarks>
    /// The header reads <c>Spam: True ; 15.0 / 5.0</c>. The verdict word is deliberately not read: it is the daemon's
    /// own comparison of the two numbers, and this deployment may be judging the score by a threshold of its own, so
    /// keeping the word would let a record state a verdict its own numbers contradict.
    /// </remarks>
    public bool TryReadAssessment(out double score, out double threshold)
    {
        score = 0;
        threshold = 0;

        if (!this.Headers.TryGetValue("Spam", out var value))
        {
            return false;
        }

        var semicolon = value.IndexOf(';', StringComparison.Ordinal);

        if (semicolon < 0)
        {
            return false;
        }

        var numbers = value[(semicolon + 1)..].Split('/', StringSplitOptions.TrimEntries);

        return numbers.Length is 2
            && TryReadNumber(numbers[0], out score)
            && TryReadNumber(numbers[1], out threshold);
    }

    private static bool TryReadStatusLine(string statusLine, [NotNullWhen(true)] out string? protocolVersion)
    {
        protocolVersion = null;

        if (!statusLine.StartsWith(StatusLinePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = statusLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2 || parts[1] != SuccessCode)
        {
            return false;
        }

        protocolVersion = parts[0][StatusLinePrefix.Length..];

        return protocolVersion.Length is not 0;
    }

    /// <summary>Reads the header block, keeping the first value where a name appears twice.</summary>
    /// <remarks>
    /// A line that is not a header is skipped rather than refused, because the protocol says a client meets headers it
    /// does not know and keeps looking rather than treating them as errors.
    /// </remarks>
    private static Dictionary<string, string> ReadHeaders(IEnumerable<string> lines)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);

            if (colon > 0)
            {
                headers.TryAdd(line[..colon].Trim(), line[(colon + 1)..].Trim());
            }
        }

        return headers;
    }

    private static bool TryReadNumber(string text, out double value) => double.TryParse(
        text,
        NumberStyles.Float,
        CultureInfo.InvariantCulture,
        out value) && double.IsFinite(value);
}
