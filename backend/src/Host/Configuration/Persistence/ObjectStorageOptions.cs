// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MailFathom.Infrastructure.ObjectStorage;
using MailFathom.Infrastructure.Secrets.Discovery;

namespace MailFathom.Host.Configuration.Persistence;

/// <summary>Declares the S3-compatible endpoint a deployment stores message content in.</summary>
/// <remarks>
/// <para>
/// Everything an operator writes here is read once, while the host composes itself, because none of it can change
/// without the client being rebuilt — except the two credential blocks, which are references resolved before every
/// request, so a key rotated behind an unchanged reference takes effect on the next call with no restart to schedule.
/// The trust anchor is the one exception to the exception, and <see cref="ObjectStorageTransportTrust" /> says why.
/// </para>
/// <para>
/// <b>The access key identifier is a secret block like the secret beside it</b>, rather than a plain string in an
/// appsettings file. It names an identity at the endpoint, it is one half of what an attacker needs, and every provider
/// that issues one issues it together with its secret from the same place — so the two are provisioned, rotated, and
/// erased by exactly the machinery every other secret here uses.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class ObjectStorageOptions
{
    /// <summary>The configuration path this block is bound from, used to name a faulty setting.</summary>
    internal const string SectionPath = $"{ContentStorageOptions.SectionName}:{nameof(ContentStorageOptions.ObjectStorage)}";

    /// <summary>The shortest connect timeout a deployment may configure.</summary>
    /// <remarks>Below a second an endpoint on an ordinary network is refused for being on an ordinary network, which is a bound nobody meant to set.</remarks>
    internal static readonly TimeSpan MinimumConnectTimeout = TimeSpan.FromSeconds(1);

    /// <summary>The longest connect timeout a deployment may configure.</summary>
    /// <remarks>A connection that has not been established in a minute is one the attempt budget above it has already given up on, so a larger value would bound nothing.</remarks>
    internal static readonly TimeSpan MaximumConnectTimeout = TimeSpan.FromMinutes(1);

    /// <summary>The shortest request timeout a deployment may configure.</summary>
    /// <remarks>A whole request covers the connection, the TLS handshake, and a payload; five seconds is where that stops being achievable for anything but an endpoint on the same host.</remarks>
    internal static readonly TimeSpan MinimumRequestTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The longest request timeout a deployment may configure.</summary>
    /// <remarks>Ten minutes is well past the largest message this system stores over the slowest link it is deployed on, and the transport is a backstop rather than the budget in any case.</remarks>
    internal static readonly TimeSpan MaximumRequestTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Gets or sets the absolute address requests are sent to.</summary>
    /// <remarks>
    /// The endpoint rather than the bucket: the bucket is named below and reached by whichever addressing style
    /// <see cref="UsePathStyleAddressing" /> selects. A plain <c>http</c> address is refused, because a request carries a
    /// signature and, on a write, the message itself.
    /// </remarks>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Gets or sets the bucket every object is written into and read from.</summary>
    public string Bucket { get; set; } = string.Empty;

    /// <summary>Gets or sets the prefix every key this deployment writes begins with.</summary>
    /// <remarks>
    /// Empty is a bucket MailFathom has to itself, which is the shape a self-hosted deployment usually takes. A prefix is
    /// what makes a shared bucket safe, and nothing here can verify that two deployments sharing one arranged disjoint
    /// prefixes — that is the operator's, and the documentation says so.
    /// </remarks>
    public string KeyPrefix { get; set; } = string.Empty;

    /// <summary>Gets or sets the region a request is signed under.</summary>
    /// <remarks>Empty takes <see cref="ObjectStorageEndpoint.DefaultRegion" />, which is what an endpoint with no notion of a region accepts. A request always carries one, because SigV4 puts a region into the credential scope whether the endpoint has one or not.</remarks>
    public string Region { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the bucket is addressed in the path rather than in the host name.</summary>
    /// <remarks>On by default, because virtual-hosted addressing needs a wildcard DNS name and a certificate to match it, which a self-hosted endpoint reached by address or by a service name inside a cluster has neither of.</remarks>
    public bool UsePathStyleAddressing { get; set; } = true;

    /// <summary>Gets or sets how long establishing a connection to the endpoint may take.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets or sets how long one whole request to the endpoint may take.</summary>
    /// <remarks>The transport's backstop rather than the operation's budget, which is <c>Resilience:ObjectStorageInvocation</c>. It is set above one attempt's timeout deliberately, so a slow endpoint is reported as the budget an operator configured rather than as a transport failure underneath it.</remarks>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(100);

    /// <summary>Gets or sets the reference to the access key identifier requests are signed as.</summary>
    /// <remarks>Absent by default rather than an empty block, so secret discovery does not find an unresolvable reference nobody wrote. It is required once the object-storage backend is selected.</remarks>
    public ConfiguredSecret? AccessKeyId { get; set; }

    /// <summary>Gets or sets the reference to the secret a request's signature is derived from.</summary>
    /// <remarks>Absent by default for the reason the identifier is, and required on the same terms.</remarks>
    public ConfiguredSecret? SecretAccessKey { get; set; }

    /// <summary>Gets or sets how often the endpoint is swept for mail nothing points at, and what a sweep leaves alone.</summary>
    /// <remarks>Present by default rather than absent, because a deployment that selected this backend is swept whether or not it wrote the block: an object nothing points at is mail nobody agreed to keep, so leaving the sweep off is not one of the things an operator may configure.</remarks>
    public ContentObjectReclamationOptions Reclamation { get; set; } = new();

    /// <summary>Gets or sets the reference to the certificate authority that signed the endpoint's certificate.</summary>
    /// <remarks>
    /// Absent for an endpoint the platform's own trust store already answers for, which is every hosted provider. It is
    /// how an endpoint the operator runs themselves is reached, and it is the only supported way: no setting anywhere
    /// turns validation off.
    /// </remarks>
    public ConfiguredSecret? TrustAnchor { get; set; }

    /// <summary>Reports every reason this endpoint could not be used, by reading the declaration alone.</summary>
    /// <returns>One message per faulty setting, each naming its configuration path, empty when the declaration is usable.</returns>
    /// <remarks>
    /// Every bound is checked here rather than through data annotations on the members above, because the block is
    /// validated only when the deployment selected this backend — an instance storing content in the database writes
    /// nothing here and must not be refused for it.
    /// </remarks>
    public IEnumerable<string> FindConfigurationErrors()
    {
        foreach (var error in this.FindAddressErrors())
        {
            yield return error;
        }

        if (string.IsNullOrWhiteSpace(this.Bucket))
        {
            yield return Error(nameof(this.Bucket), "names no bucket. The object-storage backend writes every payload into one bucket, which the deployment names here.");
        }

        if (this.KeyPrefix.Length > 0 && this.KeyPrefix.Trim().Length == 0)
        {
            yield return Error(nameof(this.KeyPrefix), "is whitespace. Leave it empty for a bucket this deployment has to itself, so a key is not written under a prefix nobody can type.");
        }

        foreach (var error in FindCredentialErrors(nameof(this.AccessKeyId), this.AccessKeyId))
        {
            yield return error;
        }

        foreach (var error in FindCredentialErrors(nameof(this.SecretAccessKey), this.SecretAccessKey))
        {
            yield return error;
        }

        foreach (var error in this.FindTimeoutErrors())
        {
            yield return error;
        }

        foreach (var error in this.Reclamation.FindConfigurationErrors())
        {
            yield return error;
        }
    }

    /// <summary>Builds the endpoint this declaration describes.</summary>
    /// <returns>The endpoint.</returns>
    /// <remarks>Called only after validation has passed, so what is left here is mapping rather than checking.</remarks>
    public ObjectStorageEndpoint ToEndpoint() => ObjectStorageEndpoint.Create(
        new Uri(this.Endpoint.Trim(), UriKind.Absolute),
        this.Bucket,
        this.KeyPrefix,
        this.Region,
        this.UsePathStyleAddressing,
        this.ConnectTimeout,
        this.RequestTimeout);

    private static IEnumerable<string> FindCredentialErrors(string propertyName, ConfiguredSecret? credential)
    {
        if (credential is null || string.IsNullOrWhiteSpace(credential.SecretReference))
        {
            yield return Error(
                propertyName,
                "references no material. The object-storage backend presents an explicit credential on every request and never resolves one from the process environment, a shared credentials file, or an instance metadata service, so a deployment that configures none is refused rather than given the host's own identity.");
        }
    }

    private static string Error(string propertyName, string detail) =>
        string.Format(CultureInfo.InvariantCulture, "{0}:{1} {2}", SectionPath, propertyName, detail);

    private IEnumerable<string> FindAddressErrors()
    {
        if (string.IsNullOrWhiteSpace(this.Endpoint))
        {
            yield return Error(nameof(this.Endpoint), "names no address. The object-storage backend is reached at an address the deployment states, because nothing may be resolved from the process environment.");

            yield break;
        }

        if (!Uri.TryCreate(this.Endpoint.Trim(), UriKind.Absolute, out var address))
        {
            yield return Error(nameof(this.Endpoint), "is not an absolute address.");

            yield break;
        }

        if (!string.Equals(address.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            yield return Error(
                nameof(this.Endpoint),
                "is not an https address. A request to the endpoint carries a signature and, on a write, the message itself, so a plain http address would publish both to anything on the path.");
        }
    }

    private IEnumerable<string> FindTimeoutErrors()
    {
        if (this.ConnectTimeout < MinimumConnectTimeout || this.ConnectTimeout > MaximumConnectTimeout)
        {
            yield return Error(
                nameof(this.ConnectTimeout),
                string.Format(
                    CultureInfo.InvariantCulture,
                    "is '{0}', which is outside the permitted range of {1} to {2}.",
                    this.ConnectTimeout,
                    MinimumConnectTimeout,
                    MaximumConnectTimeout));
        }

        if (this.RequestTimeout < MinimumRequestTimeout || this.RequestTimeout > MaximumRequestTimeout)
        {
            yield return Error(
                nameof(this.RequestTimeout),
                string.Format(
                    CultureInfo.InvariantCulture,
                    "is '{0}', which is outside the permitted range of {1} to {2}.",
                    this.RequestTimeout,
                    MinimumRequestTimeout,
                    MaximumRequestTimeout));
        }

        if (this.RequestTimeout <= this.ConnectTimeout)
        {
            yield return Error(
                nameof(this.RequestTimeout),
                "is not longer than ConnectTimeout. A whole request covers the connection it begins with, so a request budget inside the connect budget would cut every request before its connection was established.");
        }
    }
}
