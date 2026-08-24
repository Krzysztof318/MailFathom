// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Host.Configuration.Persistence;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Persistence;

/// <summary>Covers where a deployment writes the raw MIME of the messages it stores next, and when the block beneath it is judged.</summary>
public sealed class ContentStorageOptionsTests
{
    /// <summary>An absent section is the database backend, which is what every deployment that has never heard of this setting is already running.</summary>
    [Fact]
    public void Validate_ADeploymentThatConfiguresNothing_StoresContentInTheDatabase()
    {
        // Arrange
        var options = new ContentStorageOptions();

        // Act
        var results = Validate(options);

        // Assert
        Assert.Empty(results);
        Assert.Equal(ContentStorageBackend.Database, options.Backend);
        Assert.False(options.IsObjectStorageSelected);
    }

    /// <summary>
    /// The nested block takes working defaults that name no address, no bucket, and no credential, and an instance
    /// storing content in the database must never be refused for any of them.
    /// </summary>
    [Fact]
    public void Validate_TheDatabaseBackend_JudgesTheUnconfiguredObjectStorageBlockNotAtAll()
    {
        // Arrange
        var options = new ContentStorageOptions
        {
            Backend = ContentStorageBackend.Database,
            ObjectStorage = new ObjectStorageOptions { ConnectTimeout = TimeSpan.FromDays(1) },
        };

        // Act
        var results = Validate(options);

        // Assert
        Assert.Empty(results);
    }

    /// <summary>
    /// Selecting the backend is what makes the block required. A deployment that named none of an address, a bucket, or
    /// a credential must fail rather than acquire the host's own identity from the environment.
    /// </summary>
    [Fact]
    public void Validate_TheObjectStorageBackendWithAnEmptyBlock_FailsStartupNamingWhatIsMissing()
    {
        // Arrange
        var options = new ContentStorageOptions { Backend = ContentStorageBackend.ObjectStorage };

        // Act
        var results = Validate(options);

        // Assert
        Assert.All(
            results,
            result => Assert.Equal([nameof(ContentStorageOptions.ObjectStorage)], result.MemberNames));
        Assert.Contains(results, result => Names(result, nameof(ObjectStorageOptions.Endpoint)));
        Assert.Contains(results, result => Names(result, nameof(ObjectStorageOptions.Bucket)));
        Assert.Contains(results, result => Names(result, nameof(ObjectStorageOptions.AccessKeyId)));
        Assert.Contains(results, result => Names(result, nameof(ObjectStorageOptions.SecretAccessKey)));
    }

    /// <summary>
    /// A bare number binds onto the enum whether or not a member carries it, and an undefined value is not the
    /// object-storage backend, so the block beneath it would go unjudged and the deployment would quietly write to the
    /// database an operator did not select.
    /// </summary>
    [Fact]
    public void Validate_ABackendNoMemberCarries_FailsStartupNamingTheKey()
    {
        // Arrange
        var options = new ContentStorageOptions { Backend = (ContentStorageBackend)9 };

        // Act
        var results = Validate(options);

        // Assert
        var failure = Assert.Single(results);
        Assert.Equal([nameof(ContentStorageOptions.Backend)], failure.MemberNames);
        Assert.Contains("ContentStorage:Backend", failure.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains(nameof(ContentStorageBackend.Database), failure.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains(nameof(ContentStorageBackend.ObjectStorage), failure.ErrorMessage, StringComparison.Ordinal);
        Assert.False(options.IsObjectStorageSelected);
    }

    [Fact]
    public void Validate_AUsableObjectStorageBlock_IsAccepted()
    {
        // Arrange
        var options = new ContentStorageOptions
        {
            Backend = ContentStorageBackend.ObjectStorage,
            ObjectStorage = UsableEndpoint(),
        };

        // Act
        var results = Validate(options);

        // Assert
        Assert.Empty(results);
        Assert.True(options.IsObjectStorageSelected);
    }

    internal static ObjectStorageOptions UsableEndpoint() => new()
    {
        Endpoint = "https://objects.example.test:9000/",
        Bucket = "payloads",
        AccessKeyId = new ConfiguredSecret { Name = "object-storage-key-id", SecretReference = "file:key-id" },
        SecretAccessKey = new ConfiguredSecret { Name = "object-storage-secret", SecretReference = "file:secret" },
    };

    private static bool Names(ValidationResult result, string propertyName) =>
        result.ErrorMessage?.Contains($"ContentStorage:ObjectStorage:{propertyName}", StringComparison.Ordinal) is true;

    private static ValidationResult[] Validate(ContentStorageOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        return [.. results];
    }
}
