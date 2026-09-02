// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace MailFathom.Host.Configuration;

/// <summary>What this deployment is, as facts about the installation rather than about any one surface it serves.</summary>
/// <remarks>
/// <para>
/// A root of its own because the address clients reach this deployment at is not a property of the feature that first
/// needed it. Attachment download links are that feature today; anything else that has to hand a caller an absolute
/// address back — a resumable operation, a report, a webhook a mailbox rule calls — asks the same question, and an
/// operator should answer it once rather than once per consumer, under a key whose name still makes sense when the
/// second consumer arrives.
/// </para>
/// <para>
/// Nothing here is derived from a request. A URL composed from a <c>Host</c> header would let whoever called a tool
/// decide where the address it receives points, which turns an address this deployment published into one a caller
/// chose — so the value is declared, and a deployment declaring none is answered by the consumer rather than guessed at
/// on its behalf.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class DeploymentOptions : IValidatableObject
{
    /// <summary>The configuration section these settings are bound from.</summary>
    public const string SectionName = "Deployment";

    /// <summary>Gets or sets the absolute address clients reach this deployment at, or <see langword="null" /> when it declares none.</summary>
    /// <remarks>
    /// <para>
    /// There is no sensible default: only the operator knows which name a client reaches this process by, and a guess
    /// would produce addresses that resolve to nothing or, worse, to somebody else.
    /// </para>
    /// <para>
    /// It carries no path, because a consumer composes its own route beneath it and this process serves those routes at
    /// its root — an address with a path would compose something nothing answers. Clear text is refused unless the host
    /// is a loopback address, since what is composed beneath it may be a capability, and a capability in a URL is a
    /// secret in transit.
    /// </para>
    /// </remarks>
    public Uri? PublicBaseAddress { get; set; }

    /// <summary>Gets or sets whether this deployment is running read-only, in which it sends no mail at all.</summary>
    /// <remarks>
    /// <para>
    /// It is a fact about the installation rather than about an account, which is why it lives here and why no
    /// per-account setting argues with it: an operator who has said this instance may not write outward has answered
    /// for everything the instance can do, and a switch a lower level could override would be worth nothing.
    /// </para>
    /// <para>
    /// Off by default, so an existing deployment reaching this version behaves as it did. That is coherent rather than
    /// a weaker default: sending is off until an account is turned on, so an instance nobody configured to send does
    /// not send either way. What the mode changes is the kind of assurance — from a reading of the account list, which
    /// has to be re-read after every edit, into a posture the whole process holds.
    /// </para>
    /// </remarks>
    public bool ReadOnly { get; set; }

    /// <summary>Composes the address a consumer's route is served at.</summary>
    /// <param name="routePrefix">The route's path prefix, owned by the endpoint that answers it.</param>
    /// <returns>The prefix an identifier is appended to, or <see langword="null" /> when this deployment declares no address.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="routePrefix" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Composed from the declared address and the route in one place, so what a published address points at and what
    /// this process maps cannot drift apart. The trailing slash is what makes whatever follows a further segment rather
    /// than a replacement for the last one.
    /// </remarks>
    public Uri? ComposeAddressFor(string routePrefix)
    {
        ArgumentNullException.ThrowIfNull(routePrefix);

        return this.PublicBaseAddress is { } address
            ? new Uri(address, $"{routePrefix.TrimStart('/')}/")
            : null;
    }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (this.PublicBaseAddress is not { } address)
        {
            yield break;
        }

        foreach (var error in FindAddressErrors(address))
        {
            yield return new ValidationResult(error, [nameof(this.PublicBaseAddress)]);
        }
    }

    private static IEnumerable<string> FindAddressErrors(Uri address)
    {
        if (!address.IsAbsoluteUri)
        {
            yield return $"{SectionName}:{nameof(PublicBaseAddress)} is '{address}', which is not an absolute address. Write the scheme and host clients reach this deployment at, for example 'https://mail.example.test'.";

            yield break;
        }

        if (address.Scheme is not ("http" or "https"))
        {
            yield return $"{SectionName}:{nameof(PublicBaseAddress)} is '{address}', whose scheme '{address.Scheme}' is neither http nor https. What is composed beneath it is fetched over HTTP, so no other scheme can address one.";
        }
        else if (address.Scheme is "http" && !IsLoopback(address))
        {
            yield return $"{SectionName}:{nameof(PublicBaseAddress)} is '{address}', which is clear text to a host that is not loopback. An address composed beneath it may carry a capability, so issuing one over http would hand whatever it names to anything on the path.";
        }

        if (address.AbsolutePath is not ("" or "/"))
        {
            yield return $"{SectionName}:{nameof(PublicBaseAddress)} is '{address}', which carries the path '{address.AbsolutePath}'. This process serves its routes at its root, so an address composed beneath a path would address something nothing answers.";
        }

        if (address.Query.Length > 0 || address.Fragment.Length > 0)
        {
            yield return $"{SectionName}:{nameof(PublicBaseAddress)} is '{address}', which carries a query or a fragment. Neither survives into a composed address, so writing one states something this deployment cannot honor.";
        }
    }

    /// <summary>Decides whether an address names this machine, which is where clear text is a development posture rather than an exposure.</summary>
    /// <remarks><see cref="Uri.IsLoopback" /> answers for an address literal and for the reserved name; a host that resolves to a loopback address elsewhere is not one, and treating it as one would let DNS decide whether a capability travels in clear text.</remarks>
    private static bool IsLoopback(Uri address) =>
        address.IsLoopback
        || (IPAddress.TryParse(address.Host, out var literal) && IPAddress.IsLoopback(literal));
}
