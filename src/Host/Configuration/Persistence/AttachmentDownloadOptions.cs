// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;

namespace MailFathom.Host.Configuration.Persistence;

/// <summary>Configures the short-lived links a read hands back in place of an attachment's bytes.</summary>
/// <remarks>
/// <para>
/// Two settings, and they answer different kinds of question. The address is a fact about the deployment that nothing
/// can be guessed from, so it has no default and a deployment that states none issues no links at all. The lifetime is a
/// risk decision with a working answer, so it has one and is bounded on both sides.
/// </para>
/// <para>
/// A link is a bearer capability: it names one attachment of one email, it carries a signature, and it needs no
/// credential to redeem. That is what makes the window the control — a leaked URL is worth something only until it
/// expires — and it is why the ceiling here is the product's rather than the operator's.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class AttachmentDownloadOptions
{
    /// <summary>The configuration path this block is bound from, used to name a faulty setting.</summary>
    internal const string SectionPath = "EmailContent:AttachmentDownloads";

    /// <summary>The shortest lifetime a deployment may configure.</summary>
    /// <remarks>
    /// A link is useless before whatever fetches it has been handed the URL, and that hand-off crosses a protocol
    /// response, a client, and often a separate process. A minute is the smallest window in which that reliably
    /// completes; anything shorter would produce links that expire between being issued and being read.
    /// </remarks>
    internal static readonly TimeSpan MinimumLifetime = TimeSpan.FromMinutes(1);

    /// <summary>The longest lifetime a deployment may configure.</summary>
    /// <remarks>
    /// A URL is copied into proxy logs, browser history, and chat transcripts by software nobody here controls, so what
    /// bounds the damage of one leaking is how soon it dies. Half an hour keeps every issued link inside the minutes
    /// this capability is designed around; beyond that it stops being a capability and becomes a credential the
    /// deployment cannot revoke.
    /// </remarks>
    internal static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(30);

    /// <summary>Gets or sets the absolute address clients reach this deployment at, or <see langword="null" /> to issue no links.</summary>
    /// <remarks>
    /// <para>
    /// Stated rather than derived from the request, because a link composed from a <c>Host</c> header would let whoever
    /// called the tool decide where the URL it receives points. There is no sensible default: only the operator knows
    /// which name a client reaches this process by, and guessing would produce links that resolve to nothing or, worse,
    /// to somebody else.
    /// </para>
    /// <para>
    /// It must carry no path, because the download route is mapped at the root of this process and an address with a
    /// path would compose a URL nothing answers. Clear text is refused unless the host is a loopback address, since a
    /// capability in a URL is a secret in transit.
    /// </para>
    /// </remarks>
    public Uri? PublicBaseAddress { get; set; }

    /// <summary>Gets or sets how long a minted link stays redeemable.</summary>
    /// <remarks>Ten minutes unless a deployment says otherwise: long enough for a client to hand the URL to whatever fetches it, short enough that a leaked link is usually already dead.</remarks>
    public TimeSpan LinkLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Finds everything an operator must fix before links can be issued.</summary>
    /// <returns>One message per faulty setting, each naming its configuration path, empty when the settings are usable.</returns>
    /// <remarks>
    /// The lifetime is judged whether or not an address was declared, because a deployment that writes a bad one and no
    /// address would otherwise be told nothing until it added the address — and a value out of range is refused rather
    /// than clamped, the way every other unusable option here is.
    /// </remarks>
    public IReadOnlyList<string> FindConfigurationErrors()
    {
        var errors = new List<string>();

        if (this.LinkLifetime < MinimumLifetime || this.LinkLifetime > MaximumLifetime)
        {
            errors.Add(string.Format(
                CultureInfo.InvariantCulture,
                "{0}:{1} is '{2}', which is outside the permitted range of {3} to {4}. A shorter window would expire links before a client could fetch them, and a longer one would turn a capability nobody can revoke into a durable credential.",
                SectionPath,
                nameof(this.LinkLifetime),
                this.LinkLifetime,
                MinimumLifetime,
                MaximumLifetime));
        }

        if (this.PublicBaseAddress is { } address)
        {
            errors.AddRange(FindAddressErrors(address));
        }

        return errors;
    }

    /// <summary>Composes the address a capability is appended to, which is the declared address plus the download route.</summary>
    /// <param name="routePrefix">The download route's path prefix, owned by the endpoint that answers it.</param>
    /// <returns>The prefix a link is built from, or <see langword="null" /> when this deployment declares no address.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="routePrefix" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Composed here rather than inside the adapter that mints links, so the route a URL points at and the route this
    /// process maps are one decision read from one place. The trailing slash is what makes the capability a further
    /// segment rather than a replacement for the last one.
    /// </remarks>
    public Uri? ComposeDownloadAddressPrefix(string routePrefix)
    {
        ArgumentNullException.ThrowIfNull(routePrefix);

        return this.PublicBaseAddress is { } address
            ? new Uri(address, $"{routePrefix.TrimStart('/')}/")
            : null;
    }

    private static IEnumerable<string> FindAddressErrors(Uri address)
    {
        if (!address.IsAbsoluteUri)
        {
            yield return $"{SectionPath}:{nameof(PublicBaseAddress)} is '{address}', which is not an absolute address. Write the scheme and host clients reach this deployment at, for example 'https://mail.example.test'.";

            yield break;
        }

        if (address.Scheme is not ("http" or "https"))
        {
            yield return $"{SectionPath}:{nameof(PublicBaseAddress)} is '{address}', whose scheme '{address.Scheme}' is neither http nor https. A download link is fetched over HTTP, so no other scheme can address one.";
        }
        else if (address.Scheme is "http" && !IsLoopback(address))
        {
            yield return $"{SectionPath}:{nameof(PublicBaseAddress)} is '{address}', which is clear text to a host that is not loopback. A link carries a capability in its URL, so issuing one over http would hand every file it names to anything on the path.";
        }

        if (address.AbsolutePath is not ("" or "/"))
        {
            yield return $"{SectionPath}:{nameof(PublicBaseAddress)} is '{address}', which carries the path '{address.AbsolutePath}'. The download route is served at the root of this process, so a link built beneath a path would address something nothing answers.";
        }

        if (address.Query.Length > 0 || address.Fragment.Length > 0)
        {
            yield return $"{SectionPath}:{nameof(PublicBaseAddress)} is '{address}', which carries a query or a fragment. Neither survives into the composed link, so writing one states something this deployment cannot honor.";
        }
    }

    /// <summary>Decides whether an address names this machine, which is where clear text is a development posture rather than an exposure.</summary>
    /// <remarks><see cref="Uri.IsLoopback" /> answers for an address literal and for the reserved name; a host that resolves to a loopback address elsewhere is not one, and treating it as one would let DNS decide whether a capability travels in clear text.</remarks>
    private static bool IsLoopback(Uri address) =>
        address.IsLoopback
        || (IPAddress.TryParse(address.Host, out var literal) && IPAddress.IsLoopback(literal));
}
