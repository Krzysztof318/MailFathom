// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Infrastructure.Secrets.References;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Infrastructure.Secrets.Sources;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Secrets.References;

public sealed class SecretReferenceResolverTests
{
    private const string CredentialsDirectory = "/run/credentials/mailfathom.service";

    [Fact]
    public async Task ResolveAsync_SystemdCredential_ReadsTheNameFromTheCredentialsDirectory()
    {
        // Arrange
        var files = new InMemorySecretFileReader
        {
            Files = { [$"{CredentialsDirectory}/imap-primary-password"] = "hasło"u8.ToArray() },
        };
        var resolver = CreateResolver(SecretValueInterpretation.ReferenceOnly, files, WithCredentialsDirectory());

        // Act
        var result = await resolver.ResolveAsync("systemd-credential:imap-primary-password", CancellationToken.None);

        // Assert
        using var secret = result.Secret;
        Assert.Equal("hasło", secret!.RevealAsString());
    }

    [Fact]
    public async Task ResolveAsync_SystemdCredentialWithoutCredentialsDirectory_FailsWithCredentialsDirectoryUnavailable()
    {
        // Arrange
        var resolver = CreateResolver(
            SecretValueInterpretation.ReferenceOnly,
            new InMemorySecretFileReader(),
            new InMemoryEnvironmentVariableReader());

        // Act
        var result = await resolver.ResolveAsync("systemd-credential:imap-primary-password", CancellationToken.None);

        // Assert
        Assert.Equal(SecretResolutionFailure.CredentialsDirectoryUnavailable, result.Failure);
    }

    [Theory]
    [InlineData("systemd-credential:../../etc/shadow")]
    [InlineData("systemd-credential:nested/name")]
    [InlineData(@"systemd-credential:nested\name")]
    public async Task ResolveAsync_SystemdCredentialNameContainingPathSeparator_FailsWithTargetMissing(string configuredValue)
    {
        // Arrange
        var resolver = CreateResolver(
            SecretValueInterpretation.ReferenceOnly,
            new InMemorySecretFileReader(),
            WithCredentialsDirectory());

        // Act
        var result = await resolver.ResolveAsync(configuredValue, CancellationToken.None);

        // Assert
        Assert.Equal(SecretResolutionFailure.TargetMissing, result.Failure);
    }

    [Fact]
    public async Task ResolveAsync_SystemdCredentialMaterialEndsWithNewline_TrimsTheTrailingNewline()
    {
        // Arrange
        var files = new InMemorySecretFileReader
        {
            Files = { [$"{CredentialsDirectory}/imap"] = "hasło\n"u8.ToArray() },
        };
        var resolver = CreateResolver(SecretValueInterpretation.ReferenceOnly, files, WithCredentialsDirectory());

        // Act
        var result = await resolver.ResolveAsync("systemd-credential:imap", CancellationToken.None);

        // Assert
        using var secret = result.Secret;
        Assert.Equal("hasło", secret!.RevealAsString());
    }

    [Fact]
    public async Task ResolveAsync_File_ReadsTheProvisionedFile()
    {
        // Arrange
        var files = new InMemorySecretFileReader
        {
            Files = { ["/run/secrets/imap"] = "compose-secret"u8.ToArray() },
        };
        var resolver = CreateResolver(SecretValueInterpretation.ReferenceOnly, files);

        // Act
        var result = await resolver.ResolveAsync("file:/run/secrets/imap", CancellationToken.None);

        // Assert
        using var secret = result.Secret;
        Assert.Equal("compose-secret", secret!.RevealAsString());
    }

    [Fact]
    public async Task ResolveAsync_FileMissing_FailsWithMaterialNotFound()
    {
        // Arrange
        var resolver = CreateResolver(SecretValueInterpretation.ReferenceOnly, new InMemorySecretFileReader());

        // Act
        var result = await resolver.ResolveAsync("file:/run/secrets/absent", CancellationToken.None);

        // Assert
        Assert.Equal(SecretResolutionFailure.MaterialNotFound, result.Failure);
    }

    [Fact]
    public async Task ResolveAsync_FileEmpty_FailsWithMaterialEmpty()
    {
        // Arrange
        var files = new InMemorySecretFileReader { Files = { ["/run/secrets/imap"] = [] } };
        var resolver = CreateResolver(SecretValueInterpretation.ReferenceOnly, files);

        // Act
        var result = await resolver.ResolveAsync("file:/run/secrets/imap", CancellationToken.None);

        // Assert
        Assert.Equal(SecretResolutionFailure.MaterialEmpty, result.Failure);
    }

    [Fact]
    public async Task ResolveAsync_FileLargerThanTheCeiling_FailsWithMaterialTooLargeWithoutAllocatingTheMaterial()
    {
        // Arrange
        var files = new InMemorySecretFileReader
        {
            Files = { ["/var/log/enormous.log"] = new byte[8192] },
            MaximumReadableByteCount = 4096,
        };
        var resolver = CreateResolver(SecretValueInterpretation.ReferenceOnly, files);

        // Act
        var result = await resolver.ResolveAsync("file:/var/log/enormous.log", CancellationToken.None);

        // Assert
        Assert.Equal(SecretResolutionFailure.MaterialTooLarge, result.Failure);
        Assert.Null(result.Secret);
    }

    [Fact]
    public async Task ResolveAsync_EnvironmentVariable_ReadsTheVariable()
    {
        // Arrange
        var environment = new InMemoryEnvironmentVariableReader
        {
            Variables = { ["MAILFATHOM_IMAP_PASSWORD"] = "ci-password" },
        };
        var resolver = CreateResolver(SecretValueInterpretation.ReferenceOnly, new InMemorySecretFileReader(), environment);

        // Act
        var result = await resolver.ResolveAsync("env:MAILFATHOM_IMAP_PASSWORD", CancellationToken.None);

        // Assert
        using var secret = result.Secret;
        Assert.Equal("ci-password", secret!.RevealAsString());
    }

    [Fact]
    public async Task ResolveAsync_EnvironmentVariableUnset_FailsWithMaterialNotFound()
    {
        // Arrange
        var resolver = CreateResolver(SecretValueInterpretation.ReferenceOnly);

        // Act
        var result = await resolver.ResolveAsync("env:MAILFATHOM_IMAP_PASSWORD", CancellationToken.None);

        // Assert
        Assert.Equal(SecretResolutionFailure.MaterialNotFound, result.Failure);
    }

    [Fact]
    public async Task ResolveAsync_EnvironmentVariableEmpty_FailsWithMaterialEmpty()
    {
        // Arrange
        var environment = new InMemoryEnvironmentVariableReader { Variables = { ["MAILFATHOM_IMAP_PASSWORD"] = string.Empty } };
        var resolver = CreateResolver(SecretValueInterpretation.ReferenceOnly, new InMemorySecretFileReader(), environment);

        // Act
        var result = await resolver.ResolveAsync("env:MAILFATHOM_IMAP_PASSWORD", CancellationToken.None);

        // Assert
        Assert.Equal(SecretResolutionFailure.MaterialEmpty, result.Failure);
    }

    [Theory]
    [InlineData(SecretValueInterpretation.ReferenceOnly)]
    [InlineData(SecretValueInterpretation.ReferenceOrInline)]
    public async Task ResolveAsync_Plaintext_ReturnsTheLiteralInEveryModeThatParses(SecretValueInterpretation interpretation)
    {
        // Arrange
        var resolver = CreateResolver(interpretation);

        // Act
        var result = await resolver.ResolveAsync("plaintext:dev-password", CancellationToken.None);

        // Assert
        using var secret = result.Secret;
        Assert.Equal("dev-password", secret!.RevealAsString());
        Assert.Equal(SecretMaterialSource.InlineValue, result.Source);
    }

    [Fact]
    public async Task ResolveAsync_BareValueUnderReferenceOnly_FailsWithSchemeMissing()
    {
        // Arrange
        var resolver = CreateResolver(SecretValueInterpretation.ReferenceOnly);

        // Act
        var result = await resolver.ResolveAsync("a-pasted-password", CancellationToken.None);

        // Assert
        Assert.Equal(SecretResolutionFailure.SchemeMissing, result.Failure);
        Assert.Null(result.Secret);
    }

    [Fact]
    public async Task ResolveAsync_BareValueUnderReferenceOrInline_ReturnsItAsTheSecret()
    {
        // Arrange
        var resolver = CreateResolver(SecretValueInterpretation.ReferenceOrInline);

        // Act
        var result = await resolver.ResolveAsync("a-pasted-password", CancellationToken.None);

        // Assert
        using var secret = result.Secret;
        Assert.Equal("a-pasted-password", secret!.RevealAsString());
    }

    [Fact]
    public async Task ResolveAsync_EmptyTargetUnderReferenceOrInline_FailsInsteadOfBecomingTheSecret()
    {
        // Arrange
        var resolver = CreateResolver(SecretValueInterpretation.ReferenceOrInline);

        // Act
        var result = await resolver.ResolveAsync("file:", CancellationToken.None);

        // Assert
        Assert.Equal(SecretResolutionFailure.TargetMissing, result.Failure);
        Assert.Null(result.Secret);
    }

    [Fact]
    public async Task ResolveAsync_UnregisteredSchemeUnderReferenceOrInline_IsAcceptedAsTheSecret()
    {
        // Arrange
        var resolver = CreateResolver(SecretValueInterpretation.ReferenceOrInline);

        // Act
        var result = await resolver.ResolveAsync("passw:rd", CancellationToken.None);

        // Assert
        using var secret = result.Secret;
        Assert.Equal("passw:rd", secret!.RevealAsString());
        Assert.Equal(SecretMaterialSource.InlineValue, result.Source);
    }

    [Fact]
    public async Task ResolveAsync_SchemeShapedValueUnderInlineOnly_ReturnsItVerbatimWithoutParsing()
    {
        // Arrange
        var files = new InMemorySecretFileReader { Files = { ["/x"] = "provisioned"u8.ToArray() } };
        var resolver = CreateResolver(SecretValueInterpretation.InlineOnly, files);

        // Act
        var result = await resolver.ResolveAsync("file:/x", CancellationToken.None);

        // Assert
        using var secret = result.Secret;
        Assert.Equal("file:/x", secret!.RevealAsString());
    }

    [Fact]
    public async Task ResolveAsync_AbsentValueUnderInlineOnly_FailsWithReferenceMissing()
    {
        // Arrange
        var resolver = CreateResolver(SecretValueInterpretation.InlineOnly);

        // Act
        var result = await resolver.ResolveAsync(configuredValue: null, CancellationToken.None);

        // Assert
        Assert.Equal(SecretResolutionFailure.ReferenceMissing, result.Failure);
    }

    [Fact]
    public async Task ResolveAsync_AbsentValueUnderReferenceOrInline_FailsWithReferenceMissing()
    {
        // Arrange
        var resolver = CreateResolver(SecretValueInterpretation.ReferenceOrInline);

        // Act
        var result = await resolver.ResolveAsync(string.Empty, CancellationToken.None);

        // Assert
        Assert.Equal(SecretResolutionFailure.ReferenceMissing, result.Failure);
    }

    [Fact]
    public async Task ResolveAsync_RecognizedSchemeUnderReferenceOrInline_ResolvesThroughTheAdapter()
    {
        // Arrange
        var files = new InMemorySecretFileReader { Files = { ["/run/secrets/imap"] = "provisioned"u8.ToArray() } };
        var resolver = CreateResolver(SecretValueInterpretation.ReferenceOrInline, files);

        // Act
        var result = await resolver.ResolveAsync("file:/run/secrets/imap", CancellationToken.None);

        // Assert
        using var secret = result.Secret;
        Assert.Equal("provisioned", secret!.RevealAsString());
        Assert.Equal(SecretMaterialSource.SchemeAdapter, result.Source);
    }

    [Fact]
    public async Task ResolveAsync_UnregisteredScheme_FailsWithSchemeNotSupportedWithoutConsultingAnyReader()
    {
        // Arrange
        var files = new InMemorySecretFileReader();
        var resolver = CreateResolver(SecretValueInterpretation.ReferenceOnly, files);

        // Act
        var result = await resolver.ResolveAsync("azure-key-vault:imap", CancellationToken.None);

        // Assert
        Assert.Equal(SecretResolutionFailure.SchemeNotSupported, result.Failure);
        Assert.Equal(0, files.ReadCount);
    }

    [Fact]
    public async Task ResolveAsync_SchemeAdapterRegisteredOnlyByTheTest_ResolvesThroughTheSameDispatch()
    {
        // Arrange
        var resolver = new CompositeSecretReferenceResolver(
            [new TestOnlySchemeResolver()],
            new SecretResolutionOptions(SecretValueInterpretation.ReferenceOnly));

        // Act
        var result = await resolver.ResolveAsync("test-vault:imap", CancellationToken.None);

        // Assert
        using var secret = result.Secret;
        Assert.Equal("resolved-by-the-test-adapter:imap", secret!.RevealAsString());
        Assert.Equal(SecretMaterialSource.SchemeAdapter, result.Source);
    }

    [Fact]
    public async Task ResolveAsync_Cancelled_PropagatesTheCancellation()
    {
        // Arrange
        var files = new InMemorySecretFileReader { Files = { ["/run/secrets/imap"] = "provisioned"u8.ToArray() } };
        var resolver = CreateResolver(SecretValueInterpretation.ReferenceOnly, files);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act, Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            resolver.ResolveAsync("file:/run/secrets/imap", cancellation.Token));
    }

    [Fact]
    public async Task ResolveAsync_ValueAcceptedInline_ReportsInlineValueAsTheSource()
    {
        // Arrange
        var resolver = CreateResolver(SecretValueInterpretation.ReferenceOrInline);

        // Act
        var result = await resolver.ResolveAsync("a-pasted-password", CancellationToken.None);

        // Assert
        using var secret = result.Secret;
        Assert.NotNull(secret);
        Assert.Equal(SecretMaterialSource.InlineValue, result.Source);
    }

    [Theory]
    [InlineData("a-pasted-password")]
    [InlineData("file:/run/secrets/absent")]
    [InlineData("azure-key-vault:imap")]
    [InlineData("systemd-credential:imap")]
    [InlineData("env:MAILFATHOM_ABSENT")]
    [InlineData("file:")]
    public async Task ResolveAsync_EveryFailure_ReturnsNoSecretMaterial(string configuredValue)
    {
        // Arrange
        var resolver = CreateResolver(SecretValueInterpretation.ReferenceOnly);

        // Act
        var result = await resolver.ResolveAsync(configuredValue, CancellationToken.None);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Null(result.Secret);
        Assert.NotNull(result.Failure);
        Assert.DoesNotContain("secrets", result.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SecretValueInterpretation.ReferenceOrInline)]
    [InlineData(SecretValueInterpretation.InlineOnly)]
    public async Task ResolveAsync_InlineValueAboveTheCeiling_FailsInsteadOfPinningIt(
        SecretValueInterpretation interpretation)
    {
        // Arrange
        var resolver = CreateResolver(interpretation);
        var oversizedValue = new string('p', SecretMaterialLimits.MaximumMaterialByteCount + 1);

        // Act
        var result = await resolver.ResolveAsync(oversizedValue, CancellationToken.None);

        // Assert
        Assert.Equal(SecretResolutionFailure.MaterialTooLarge, result.Failure);
        Assert.Null(result.Secret);
    }

    [Fact]
    public async Task ResolveAsync_EnvironmentValueAboveTheCeiling_FailsLikeAnyOtherOversizedMaterial()
    {
        // Arrange
        var environment = new InMemoryEnvironmentVariableReader
        {
            Variables = { ["MAILFATHOM_OVERSIZED"] = new string('p', SecretMaterialLimits.MaximumMaterialByteCount + 1) },
        };
        var resolver = CreateResolver(SecretValueInterpretation.ReferenceOnly, new InMemorySecretFileReader(), environment);

        // Act
        var result = await resolver.ResolveAsync("env:MAILFATHOM_OVERSIZED", CancellationToken.None);

        // Assert
        Assert.Equal(SecretResolutionFailure.MaterialTooLarge, result.Failure);
        Assert.Null(result.Secret);
    }

    [Fact]
    public async Task ResolveAsync_PlaintextLiteralAboveTheCeiling_FailsLikeAnyOtherOversizedMaterial()
    {
        // Arrange
        var resolver = CreateResolver(SecretValueInterpretation.ReferenceOnly);
        var oversizedLiteral = new string('p', SecretMaterialLimits.MaximumMaterialByteCount + 1);

        // Act
        var result = await resolver.ResolveAsync($"plaintext:{oversizedLiteral}", CancellationToken.None);

        // Assert
        Assert.Equal(SecretResolutionFailure.MaterialTooLarge, result.Failure);
        Assert.Null(result.Secret);
    }

    private static InMemoryEnvironmentVariableReader WithCredentialsDirectory() => new()
    {
        Variables = { ["CREDENTIALS_DIRECTORY"] = CredentialsDirectory },
    };

    private static CompositeSecretReferenceResolver CreateResolver(
        SecretValueInterpretation interpretation,
        InMemorySecretFileReader? files = null,
        InMemoryEnvironmentVariableReader? environment = null)
    {
        var fileReader = files ?? new InMemorySecretFileReader();
        var environmentReader = environment ?? new InMemoryEnvironmentVariableReader();

        return new CompositeSecretReferenceResolver(
            [
                new SystemdCredentialSecretReferenceResolver(environmentReader, fileReader),
                new FileSecretReferenceResolver(fileReader),
                new EnvironmentVariableSecretReferenceResolver(environmentReader),
                new PlaintextSecretReferenceResolver(),
            ],
            new SecretResolutionOptions(interpretation));
    }

    private sealed class InMemorySecretFileReader : ISecretFileReader
    {
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

        public int MaximumReadableByteCount { get; set; } = int.MaxValue;

        public int ReadCount { get; private set; }

        public async Task<SecretResolutionResult> ReadAsync(
            string path,
            int maximumByteCount,
            CancellationToken cancellationToken)
        {
            this.ReadCount++;
            cancellationToken.ThrowIfCancellationRequested();

            if (!this.Files.TryGetValue(path, out var material))
            {
                return SecretResolutionResult.Failed(SecretResolutionFailure.MaterialNotFound);
            }

            using var source = new MemoryStream(material, writable: false);

            return await BoundedSecretMaterialReader.ReadAsync(
                source,
                Math.Min(maximumByteCount, this.MaximumReadableByteCount),
                cancellationToken);
        }
    }

    private sealed class InMemoryEnvironmentVariableReader : IEnvironmentVariableReader
    {
        public Dictionary<string, string> Variables { get; } = new(StringComparer.Ordinal);

        public string? GetValue(string name) => this.Variables.TryGetValue(name, out var value) ? value : null;
    }

    /// <summary>Proves that a scheme the production code never heard of resolves through the same dispatch.</summary>
    private sealed class TestOnlySchemeResolver : ISecretSchemeResolver
    {
        public SecretReferenceScheme Scheme { get; } = SecretReferenceScheme.Create("test-vault");

        public Task<SecretResolutionResult> ResolveAsync(SecretReference reference, CancellationToken cancellationToken) =>
            Task.FromResult(SecretResolutionResult.Resolved(
                ResolvedSecret.FromBytes(Encoding.UTF8.GetBytes($"resolved-by-the-test-adapter:{reference.Target}")),
                SecretMaterialSource.SchemeAdapter));
    }
}
