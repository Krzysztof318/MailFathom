// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Amazon.Runtime;
using Amazon.S3;
using MailFathom.Infrastructure.ObjectStorage;
using MailFathom.Infrastructure.Secrets.Resolution;
using NSubstitute;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.ObjectStorage;

/// <summary>
/// Covers the one property the whole adapter exists for: nothing about the credential, the address, or the region is
/// ever left for the AWS client to discover.
/// </summary>
/// <remarks>
/// Two of these tests put AWS's own settings into the process environment while they run, which is the state a
/// deployment on EC2, ECS, or a developer machine with a shared credentials file is actually in. xUnit runs the tests of
/// one class one at a time, and nothing outside this class reads them, so the variables are set and removed inside the
/// test that needs them.
/// </remarks>
public sealed class S3ObjectStorageClientFactoryTests
{
    private static readonly ObjectStorageEndpoint Endpoint = ObjectStorageEndpoint.Create(
        new Uri("https://objects.example.test:9000/"),
        "payloads",
        "mailfathom",
        "eu-central-1",
        usePathStyleAddressing: true,
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(100));

    /// <summary>
    /// The acceptance the adapter is built around. A deployment that configured no credential must fail, rather than
    /// fall through to the SDK's own chain and sign as whatever identity the host carries — which on a cloud instance is
    /// a real one, reached by a request to a metadata service on a network MailFathom was never told about.
    /// </summary>
    [Fact]
    public async Task OpenAsync_ADeploymentMissingACredential_FailsRatherThanAcquiringTheHostsAmbientIdentity()
    {
        // Arrange
        using var ambientEnvironment = AmbientAwsSettings.Applied();
        var refusal = new InvalidOperationException(
            "ContentStorage:ObjectStorage:AccessKeyId could not be resolved [ReferenceMissing].");
        var credentialSource = Substitute.For<IObjectStorageCredentialSource>();
        credentialSource.ResolveAsync(Arg.Any<CancellationToken>()).Returns<Task<ObjectStorageCredential>>(
            _ => throw refusal);

        var factory = FactoryOver(credentialSource);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.OpenAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Same(refusal, failure);
    }

    /// <summary>
    /// The environment's own address, region, and retry mode are all things the SDK reads when it is not told them, and
    /// each of them is told here. An endpoint URL in the environment is the one that would silently redirect a
    /// configured address, which is what <c>IgnoreConfiguredEndpointUrls</c> closes.
    /// </summary>
    [Fact]
    public async Task OpenAsync_AConfiguredEndpoint_AddressesAndSignsFromTheConfigurationRatherThanTheEnvironment()
    {
        // Arrange
        using var ambientEnvironment = AmbientAwsSettings.Applied();
        var factory = FactoryOver(CredentialSourceAnswering());

        // Act
        using var openedClient = await factory.OpenAsync(TestContext.Current.CancellationToken);

        // Assert
        var configuration = Assert.IsType<AmazonS3Config>(openedClient.Client.Config);

        Assert.Equal(Endpoint.Address.ToString(), configuration.ServiceURL);
        Assert.Equal("eu-central-1", configuration.AuthenticationRegion);
        Assert.Null(configuration.RegionEndpoint);
        Assert.True(configuration.IgnoreConfiguredEndpointUrls);
        Assert.False(configuration.EndpointDiscoveryEnabled);
        Assert.True(configuration.ForcePathStyle);
    }

    /// <summary>
    /// Every call already runs under the object-storage resilience pipeline, which decides what may be repeated from the
    /// classification beside it. A second layer here would repeat a refused signature that pipeline had ruled terminal.
    /// </summary>
    [Fact]
    public async Task OpenAsync_TheOpenedClient_CarriesNoRetryLayerOfItsOwn()
    {
        // Arrange
        var factory = FactoryOver(CredentialSourceAnswering());

        // Act
        using var openedClient = await factory.OpenAsync(TestContext.Current.CancellationToken);

        // Assert
        var configuration = Assert.IsType<AmazonS3Config>(openedClient.Client.Config);

        Assert.Equal(0, configuration.MaxErrorRetry);
        Assert.False(configuration.ThrottleRetries);
        Assert.Equal(RequestRetryMode.Standard, configuration.RetryMode);
    }

    /// <summary>
    /// Version 4 computes a CRC-32 for every upload and sends it in a trailer, which several self-hosted S3-compatible
    /// endpoints reject outright. A checksum an operation actually wants is asked for on the request instead.
    /// </summary>
    [Fact]
    public async Task OpenAsync_TheOpenedClient_ChecksumsOnlyWhereAnOperationAsksFor()
    {
        // Arrange
        var factory = FactoryOver(CredentialSourceAnswering());

        // Act
        using var openedClient = await factory.OpenAsync(TestContext.Current.CancellationToken);

        // Assert
        var configuration = Assert.IsType<AmazonS3Config>(openedClient.Client.Config);

        Assert.Equal(RequestChecksumCalculation.WHEN_REQUIRED, configuration.RequestChecksumCalculation);
        Assert.Equal(ResponseChecksumValidation.WHEN_REQUIRED, configuration.ResponseChecksumValidation);
    }

    /// <summary>The endpoint's answers are the one place a response could carry an object key, and a key names a message.</summary>
    [Fact]
    public async Task OpenAsync_TheOpenedClient_LogsNoResponse()
    {
        // Arrange
        var factory = FactoryOver(CredentialSourceAnswering());

        // Act
        using var openedClient = await factory.OpenAsync(TestContext.Current.CancellationToken);

        // Assert
        var configuration = Assert.IsType<AmazonS3Config>(openedClient.Client.Config);

        Assert.False(configuration.LogResponse);
        Assert.False(configuration.LogMetrics);
        Assert.Equal(Endpoint.RequestTimeout, configuration.Timeout);
    }

    /// <summary>
    /// Every outbound request in this process travels over a client the factory built under a named registration, which
    /// is where the timeout, the redirect policy, and the TLS trust live. The SDK would otherwise construct one of its
    /// own and none of that would apply.
    /// </summary>
    [Fact]
    public async Task OpenAsync_TheOpenedClient_SendsOverTheNamedOutboundRegistration()
    {
        // Arrange
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        using var transport = new HttpClient();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(transport);

        var factory = new S3ObjectStorageClientFactory(Endpoint, CredentialSourceAnswering(), httpClientFactory);

        // Act
        using var openedClient = await factory.OpenAsync(TestContext.Current.CancellationToken);
        var configuration = Assert.IsType<AmazonS3Config>(openedClient.Client.Config);
        configuration.HttpClientFactory.CreateHttpClient(configuration);

        // Assert
        httpClientFactory.Received(1).CreateClient(ObjectStorageEndpoint.TransportName);
    }

    /// <summary>
    /// The SDK caches nothing on top of the client factory, and disposes what it was handed. A second cache would hold a
    /// handler chain past the point the factory retired it, which is exactly the rotation opening a client per operation
    /// exists to pick up.
    /// </summary>
    [Fact]
    public async Task OpenAsync_TheOpenedClient_LeavesTheHandlerChainToTheClientFactory()
    {
        // Arrange
        var factory = new S3ObjectStorageClientFactory(
            Endpoint,
            CredentialSourceAnswering(),
            Substitute.For<IHttpClientFactory>());

        // Act
        using var openedClient = await factory.OpenAsync(TestContext.Current.CancellationToken);
        var configuration = Assert.IsType<AmazonS3Config>(openedClient.Client.Config);

        // Assert
        Assert.False(configuration.HttpClientFactory.UseSDKHttpClientCaching(configuration));
        Assert.True(configuration.HttpClientFactory.DisposeHttpClientsAfterUse(configuration));
    }

    /// <summary>The credential is released with the client, which bounds the window a process dump could hold it in to one operation.</summary>
    [Fact]
    public async Task Dispose_AnOpenedClient_ReleasesTheCredentialItPresented()
    {
        // Arrange
        var accessKeyIdMaterial = ResolvedSecret.FromText("AKIAEXAMPLEIDENTIFIER");
        var secretAccessKeyMaterial = ResolvedSecret.FromText("an-example-signing-secret");
        var credentialSource = Substitute.For<IObjectStorageCredentialSource>();
        credentialSource.ResolveAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(ObjectStorageCredential.Create(accessKeyIdMaterial, secretAccessKeyMaterial)));

        var factory = FactoryOver(credentialSource);

        // Act
        var openedClient = await factory.OpenAsync(TestContext.Current.CancellationToken);
        openedClient.Dispose();

        // Assert
        Assert.Throws<ObjectDisposedException>(() => accessKeyIdMaterial.RevealAsString());
        Assert.Throws<ObjectDisposedException>(() => secretAccessKeyMaterial.RevealAsString());
    }

    [Fact]
    public void Construction_MissingCollaborator_IsRefused()
    {
        // Arrange
        var credentialSource = Substitute.For<IObjectStorageCredentialSource>();
        var httpClientFactory = Substitute.For<IHttpClientFactory>();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => new S3ObjectStorageClientFactory(null!, credentialSource, httpClientFactory));
        Assert.Throws<ArgumentNullException>(
            () => new S3ObjectStorageClientFactory(Endpoint, null!, httpClientFactory));
        Assert.Throws<ArgumentNullException>(
            () => new S3ObjectStorageClientFactory(Endpoint, credentialSource, null!));
    }

    private static S3ObjectStorageClientFactory FactoryOver(IObjectStorageCredentialSource credentialSource) =>
        new(Endpoint, credentialSource, Substitute.For<IHttpClientFactory>());

    private static IObjectStorageCredentialSource CredentialSourceAnswering()
    {
        var credentialSource = Substitute.For<IObjectStorageCredentialSource>();
        credentialSource.ResolveAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(
            ObjectStorageCredential.Create(
                ResolvedSecret.FromText("AKIAEXAMPLEIDENTIFIER"),
                ResolvedSecret.FromText("an-example-signing-secret"))));

        return credentialSource;
    }

    /// <summary>Puts the settings the SDK resolves an identity, a region, and an address from into the process environment.</summary>
    private sealed class AmbientAwsSettings : IDisposable
    {
        private static readonly string[] VariableNames =
        [
            "AWS_ACCESS_KEY_ID",
            "AWS_SECRET_ACCESS_KEY",
            "AWS_REGION",
            "AWS_DEFAULT_REGION",
            "AWS_ENDPOINT_URL",
            "AWS_ENDPOINT_URL_S3",
        ];

        private readonly Dictionary<string, string?> replacedValues;

        private AmbientAwsSettings(Dictionary<string, string?> replacedValues) =>
            this.replacedValues = replacedValues;

        internal static AmbientAwsSettings Applied()
        {
            var replacedValues = VariableNames.ToDictionary(
                name => name,
                Environment.GetEnvironmentVariable);

            Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", "AKIAAMBIENTIDENTITY");
            Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", "an-ambient-signing-secret");
            Environment.SetEnvironmentVariable("AWS_REGION", "ap-southeast-2");
            Environment.SetEnvironmentVariable("AWS_DEFAULT_REGION", "ap-southeast-2");
            Environment.SetEnvironmentVariable("AWS_ENDPOINT_URL", "https://elsewhere.example.test/");
            Environment.SetEnvironmentVariable("AWS_ENDPOINT_URL_S3", "https://elsewhere.example.test/");

            return new AmbientAwsSettings(replacedValues);
        }

        public void Dispose()
        {
            foreach (var (name, value) in this.replacedValues)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
