// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Amazon.Runtime;
using Amazon.S3;

namespace MailFathom.Infrastructure.ObjectStorage;

/// <summary>Builds the AWS client one operation against the configured S3-compatible endpoint is made through.</summary>
/// <remarks>
/// <para>
/// <b>Every ambient resolution the SDK offers is switched off here.</b> The constructors that take no configuration
/// reach environment variables, a shared credentials file, and an instance metadata service for both the credential and
/// the region, so a deployment that forgot to configure one would quietly acquire the host's own identity and send a
/// request to a metadata endpoint on a network MailFathom was never told about. What is used instead is the constructor
/// taking explicit credentials and an explicit configuration, with the address, the signing region, and the addressing
/// style all set from what an operator wrote. <see cref="ClientConfig.IgnoreConfiguredEndpointUrls" /> closes the last
/// of it, which is the environment's own ability to redirect a configured address.
/// See <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0017-object-storage-content-backend-consistency-and-object-identity.md">ADR 0017</see> § 8.
/// </para>
/// <para>
/// <b>The SDK's own retry is off, and that is the single-layer rule rather than a preference.</b> Every call runs under
/// the <see cref="Application.Resilience.OutboundDependency.ObjectStorageInvocation" /> pipeline, which decides what may
/// be repeated from the classification beside it; a layer beneath that would repeat a refused signature the pipeline had
/// already ruled terminal, and would multiply two attempt counts against an endpoint that is already refusing.
/// </para>
/// <para>
/// The checksum behaviour is pinned to what the widest set of S3-compatible endpoints accepts rather than to the SDK's
/// default. Version 4 computes a CRC-32 for every upload and sends it in a trailer, which several self-hosted
/// implementations reject outright; a checksum an operation actually wants is asked for on the request, which is what
/// ADR 0017 § 2 has the content store do with SHA-256.
/// </para>
/// </remarks>
internal sealed class S3ObjectStorageClientFactory : IObjectStorageClientFactory
{
    private readonly IObjectStorageCredentialSource credentialSource;
    private readonly ObjectStorageTransportClientFactory transportClientFactory;

    /// <summary>Initializes the factory for one deployment's endpoint.</summary>
    /// <param name="endpoint">Where the endpoint is and how a request to it is addressed.</param>
    /// <param name="credentialSource">Resolves the access key each operation is signed with.</param>
    /// <param name="httpClientFactory">Supplies the named outbound transport every request travels over.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public S3ObjectStorageClientFactory(
        ObjectStorageEndpoint endpoint,
        IObjectStorageCredentialSource credentialSource,
        IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(credentialSource);
        ArgumentNullException.ThrowIfNull(httpClientFactory);

        this.Endpoint = endpoint;
        this.credentialSource = credentialSource;
        this.transportClientFactory = new ObjectStorageTransportClientFactory(httpClientFactory);
    }

    /// <inheritdoc />
    public ObjectStorageEndpoint Endpoint { get; }

    /// <inheritdoc />
    public async Task<OpenedObjectStorageClient> OpenAsync(CancellationToken cancellationToken)
    {
        var credential = await this.credentialSource.ResolveAsync(cancellationToken);

        try
        {
            var client = new AmazonS3Client(
                new BasicAWSCredentials(credential.AccessKeyId, credential.SecretAccessKey),
                this.CreateConfiguration());

            return new OpenedObjectStorageClient(client, client, credential);
        }
        catch
        {
            credential.Dispose();

            throw;
        }
    }

    private AmazonS3Config CreateConfiguration() => new()
    {
        // Setting the address is also what keeps a region endpoint out: the two are mutually exclusive on the
        // configuration and whichever is set last resets the other, so an explicit address leaves nothing for the SDK to
        // derive an AWS host name from.
        ServiceURL = this.Endpoint.Address.ToString(),
        ForcePathStyle = this.Endpoint.UsePathStyleAddressing,
        AuthenticationRegion = this.Endpoint.Region,
        IgnoreConfiguredEndpointUrls = true,
        EndpointDiscoveryEnabled = false,
        DisableHostPrefixInjection = true,
        HttpClientFactory = this.transportClientFactory,

        // Pinned rather than left to be read from AWS_RETRY_MODE and a shared configuration file, so the count below is
        // the whole retry story wherever this runs.
        RetryMode = RequestRetryMode.Standard,
        MaxErrorRetry = 0,
        ThrottleRetries = false,

        // Pinned for the same reason: Auto asks the environment which mode applies, which is one more thing a
        // deployment would be configuring without knowing it.
        DefaultConfigurationMode = DefaultConfigurationMode.Standard,
        RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
        ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,

        // The transport's own backstop, above one attempt's budget. What bounds an operation is the resilience
        // pipeline; a request cut here would report a transport failure where an operator configured a budget.
        Timeout = this.Endpoint.RequestTimeout,

        // The endpoint's answers are the one place a response could carry an object key, and a key names a row that
        // names a message. Neither is written to a log by this process.
        LogResponse = false,
        LogMetrics = false,
    };
}
