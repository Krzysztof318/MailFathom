// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.RegularExpressions;

namespace MailFathom.Infrastructure.Spam;

/// <summary>Names the rule corpus a scan ran under, from the only thing the daemon will say about itself.</summary>
/// <remarks>
/// <para>
/// A classification records the corpus its deciding stage ran under so that a later one can be compared against it, and
/// the spam protocol carries no such identity: the answer to a scan is a score, a threshold, and a list of rule names,
/// and the version in the status line is the version of the conversation rather than of the rules. What the daemon does
/// state is the release it is, in the <c>X-Spam-Checker-Version</c> header it adds to a message it rewrites — so the
/// identity is established once, by asking it to rewrite one synthetic message while the host is starting, and every
/// scan afterwards is stamped with the answer.
/// </para>
/// <para>
/// It names the release rather than the rule updates fetched into it, because nothing on the wire distinguishes those.
/// That is the honest limit of what a scan can claim, and it is the value that moves when an operator upgrades the
/// image, which is the comparison the record exists for. How fresh the rules behind it are is a property of the
/// deployment, which states it.
/// </para>
/// </remarks>
internal static partial class SpamAssassinCorpus
{
    /// <summary>The header a rewriting command adds, naming the release that scanned the message.</summary>
    /// <remarks>
    /// Added by the default configuration rather than by the protocol, so a daemon whose own configuration removes it
    /// leaves nothing to read and the protocol version is what identifies the corpus instead.
    /// </remarks>
    public const string CheckerVersionHeader = "X-Spam-Checker-Version";

    /// <summary>The greatest length either part of a version is read up to.</summary>
    /// <remarks>
    /// The value is written by a separate process, and it is stored on every signal a scan produces under a bound of its
    /// own. Reading a bounded prefix keeps a daemon that answered with something unexpected from being the reason a
    /// classification cannot be recorded.
    /// </remarks>
    private const int MaximumVersionPartLength = 24;

    /// <summary>Reads the corpus identity out of what a rewriting command answered.</summary>
    /// <param name="reply">The daemon's answer to the command that rewrites a message's headers.</param>
    /// <returns>The revision to stamp every scan with.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reply" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The host name the header ends with is deliberately dropped. It names the container the daemon happens to run in
    /// rather than anything about the rules, it would differ between two deployments scanning identically, and it is a
    /// host name — which <c>src/AGENTS.md</c> keeps out of what this system records about itself.
    /// </remarks>
    public static string Identify(SpamdReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        var declared = DeclaredVersion(reply.Body);

        return declared is null
            ? string.Format(CultureInfo.InvariantCulture, "spamassassin+spamd.{0}", Bounded(reply.ProtocolVersion))
            : declared;
    }

    private static string? DeclaredVersion(string rewrittenHeaders)
    {
        var match = CheckerVersion.Match(rewrittenHeaders);

        if (!match.Success)
        {
            return null;
        }

        var release = Bounded(match.Groups["release"].Value);
        var build = match.Groups["build"];

        return build.Success
            ? string.Format(CultureInfo.InvariantCulture, "spamassassin.{0}+{1}", release, Bounded(build.Value))
            : string.Format(CultureInfo.InvariantCulture, "spamassassin.{0}", release);
    }

    private static string Bounded(string part) => part.Length > MaximumVersionPartLength
        ? part[..MaximumVersionPartLength]
        : part;

    /// <summary>Matches the release and the build date in <c>X-Spam-Checker-Version: SpamAssassin 4.0.2 (2025-08-27) on host</c>.</summary>
    /// <remarks>
    /// Both captured parts exclude whitespace and brackets, so nothing a daemon writes can widen them into the rest of
    /// the line, and the build date is optional because it is the release's own annotation rather than a protocol field.
    /// </remarks>
    [GeneratedRegex(
        @"^X-Spam-Checker-Version:[ \t]*SpamAssassin[ \t]+(?<release>[^\s()]+)(?:[ \t]+\((?<build>[^\s()]+)\))?",
        RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex CheckerVersion { get; }
}
