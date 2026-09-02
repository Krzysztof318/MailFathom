// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets;

namespace MailFathom.Infrastructure.Security.Transport;

/// <summary>Whose capacity a request spends, on which transport surface.</summary>
/// <remarks>
/// <para>
/// A per-caller limit needs a name to keep the count under, and which name it is decides whether the limit protects the
/// surface or becomes the way to bring it down. Every partition a request can name is a dictionary entry the process
/// keeps for as long as the limiter does, so a key an attacker chooses is memory an attacker allocates. This type
/// therefore admits exactly one source of names: the identity authentication established. Nothing a caller writes into
/// a header, a path, a query string, an <c>Origin</c>, or a user agent reaches a key, and no forwarded address does
/// either, because a proxy header is chosen by whoever is upstream and trusting one is a separate design with its own
/// review.
/// </para>
/// <para>
/// The surface leads every key, which is what keeps two endpoints' capacity independent no matter what they are
/// configured with. A key list an operator wrote for the administrative endpoint may spell the same names as the MCP
/// endpoint's — the two sections are configured separately and neither consults the other — and without the prefix a
/// client named identically on both would spend one bucket from both surfaces. It also holds for the anonymous
/// partition, which is the one every surface has and the one a flood of bad credentials lands in.
/// </para>
/// <para>
/// Everything else shares one anonymous partition per surface. That is deliberately coarse: under
/// <c>None</c> there is no identity to tell one caller from another, so a single bucket is the only bound that cannot be
/// grown by asking for it, and unauthenticated callers being able to exhaust each other's capacity is a smaller problem
/// than unauthenticated callers being able to exhaust the process's memory. The same partition absorbs a request whose
/// credential was refused, so a flood of bad credentials is counted and limited rather than served for free.
/// </para>
/// <para>
/// Two identities can be established at once, and the rule between them is fixed rather than merged. A configured API
/// key names one client of this deployment, which is what the operator partitioned their clients into; a client
/// certificate profile names a client *application*, and several keys may sit behind one profile. Taking the key
/// wherever there is one therefore keeps the partitions exactly as narrow as the key list, while taking the profile
/// would let one key starve another that happens to share its certificate. Combining the two is the rule not taken: a
/// pair-shaped key would hand the same credential a second bucket for every profile it could present under, which is
/// capacity bought by holding one more certificate.
/// </para>
/// <para>
/// The remote address is deliberately not part of any key, not even as a fallback. It is spoofable on the traffic this
/// is aimed at, and one client behind a shared address would otherwise be limited by another's behaviour.
/// </para>
/// </remarks>
public static class TransportRateLimitPartitions
{
    /// <summary>The one identity every request on a surface without an authenticated identity is counted under.</summary>
    /// <remarks>
    /// <para>
    /// The angle brackets are what keep it from being a client's partition as well. An authenticated request is counted
    /// under the configured name itself, and <see cref="SecretName" /> accepts letters, digits, dots, hyphens, and
    /// underscores — so a sentinel spelled in those characters could be claimed by an operator who named a key after it,
    /// which would hand that one client the whole unauthenticated stream's capacity and hand unauthenticated callers
    /// theirs. A bracketed value cannot be spelled as a name at all, and a test holds that grammar to it.
    /// </para>
    /// <para>
    /// The value never appears in a response. It is part of a dictionary key inside one process, named so a log or a
    /// test can refer to it.
    /// </para>
    /// </remarks>
    public const string AnonymousIdentity = "<anonymous>";

    /// <summary>Separates the surface from the identity, in a character neither of them can contain.</summary>
    /// <remarks>
    /// A surface name is one of MailFathom's own, and an identity is either a configured name — which
    /// <see cref="SecretName" /> restricts to letters, digits, dots, hyphens, and underscores — or a bracketed sentinel.
    /// None of them can hold this character, so no pair of surface and identity can be spelled as a different pair.
    /// </remarks>
    private const char SurfaceSeparator = '|';

    /// <summary>Names the partition a request's capacity is taken from.</summary>
    /// <param name="surfaceName">The name of the transport surface the request arrived on.</param>
    /// <param name="authenticatedClientName">The name of the credential the request authenticated with, or <see langword="null" /> when it authenticated with none.</param>
    /// <param name="matchedCertificateProfileName">The name of the trust profile the connection certificate matched, or <see langword="null" /> when no certificate identified a client application.</param>
    /// <returns>The surface's name, followed by the configured name of the authenticated client, a partition of the identified client application, or <see cref="AnonymousIdentity" />.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="surfaceName" /> is <see langword="null" />, empty, or white space.</exception>
    /// <remarks>
    /// <para>
    /// Both identity names are MailFathom's own configured identities, never a credential and never anything a
    /// certificate carried. They are chosen by the operator who wrote the configuration, so the partitions an identified
    /// surface keeps number no more than its key list plus its profile list. A blank name is treated as no name at all
    /// rather than as a partition of its own.
    /// </para>
    /// <para>
    /// A profile's partition is bracketed and a key's is not, because the two grammars are the same — both accept
    /// letters, digits, dots, dashes, and underscores — so a profile and a key sharing a name would otherwise share a
    /// bucket. Under <c>ApiKey</c> both kinds occur at once, since a request whose credential was refused still reaches
    /// this with the profile its certificate matched.
    /// </para>
    /// </remarks>
    public static string KeyFor(
        string surfaceName,
        string? authenticatedClientName,
        string? matchedCertificateProfileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surfaceName);

        return $"{surfaceName}{SurfaceSeparator}{IdentityFor(authenticatedClientName, matchedCertificateProfileName)}";
    }

    /// <summary>Names the identity half of a partition key, without the surface it belongs to.</summary>
    private static string IdentityFor(string? authenticatedClientName, string? matchedCertificateProfileName)
    {
        if (!string.IsNullOrWhiteSpace(authenticatedClientName))
        {
            return authenticatedClientName;
        }

        return string.IsNullOrWhiteSpace(matchedCertificateProfileName)
            ? AnonymousIdentity
            : $"<profile:{matchedCertificateProfileName}>";
    }
}
