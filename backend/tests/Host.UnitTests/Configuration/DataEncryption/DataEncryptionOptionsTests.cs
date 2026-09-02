// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Host.Configuration.DataEncryption;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.DataEncryption;

/// <summary>Covers the rules a key ring can be judged on by reading it, without resolving anything.</summary>
/// <remarks>
/// Each refusal exists to move a mistake to startup. Whether the material behind a reference is actually a key is a
/// separate check, because it needs resolution, and it is covered where the secret configuration is validated.
/// </remarks>
public sealed class DataEncryptionOptionsTests
{
    [Fact]
    public void Validate_AWellFormedRing_ReportsNothing()
    {
        // Arrange
        var options = RingOf(("2026-08", "systemd-credential:mailfathom-data-key"));
        options.ActiveKeyId = "2026-08";

        // Act
        var results = Validate(options);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Validate_ARingWithSeveralKeys_ReportsNothing()
    {
        // Arrange — the state a deployment is in mid-rotation, which has to be a valid configuration rather than one
        // that only passes once the previous key is gone.
        var options = RingOf(
            ("2026-08", "systemd-credential:mailfathom-data-key"),
            ("2026-02", "systemd-credential:mailfathom-data-key-previous"));
        options.ActiveKeyId = "2026-08";

        // Act
        var results = Validate(options);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Validate_AnAbsentSection_ReportsNothing()
    {
        // Arrange — a deployment that seals nothing needs no ring, and no stored value carries a key identifier yet.
        // Requiring one here would refuse to start every deployment that has no use for a key.
        var options = new DataEncryptionOptions();

        // Act
        var results = Validate(options);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Validate_AnActiveKeyWithNoRingAtAll_IsRefusedNamingTheGeneratingCommand()
    {
        // Arrange — an operator who named an active key meant to provision one, so the silence of an absent ring is a
        // mistake here rather than the deliberate absence above.
        var options = new DataEncryptionOptions { ActiveKeyId = "2026-08" };

        // Act
        var results = Validate(options);

        // Assert
        var result = Assert.Single(results);
        Assert.Contains("openssl rand -base64 32", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_AnActiveKeyNoConfiguredKeyDeclares_IsRefusedListingTheConfiguredOnes()
    {
        // Arrange — the mistake a half-finished rotation leaves behind: the new identifier is typed into ActiveKeyId
        // before the key itself is added.
        var options = RingOf(("2026-02", "systemd-credential:mailfathom-data-key"));
        options.ActiveKeyId = "2026-08";

        // Act
        var results = Validate(options);

        // Assert
        var result = Assert.Single(results);
        Assert.Contains("'2026-08'", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("'2026-02'", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_AnAbsentActiveKey_IsRefusedEvenWhereTheRingHoldsOne()
    {
        // Arrange — the ring holding exactly one key does not make the selection implicit, because the value that
        // selects it is what a rotation later moves.
        var options = RingOf(("2026-08", "systemd-credential:mailfathom-data-key"));

        // Act
        var results = Validate(options);

        // Assert
        Assert.Single(results);
    }

    [Fact]
    public void Validate_TwoKeysSharingOneIdentifier_AreRefused()
    {
        // Arrange — a stored value names its key by the identifier, so two keys sharing one would leave which key opens
        // a value undecidable.
        var options = RingOf(
            ("2026-08", "systemd-credential:mailfathom-data-key"),
            ("2026-08", "systemd-credential:mailfathom-data-key-previous"));
        options.ActiveKeyId = "2026-08";

        // Act
        var results = Validate(options);

        // Assert
        var result = Assert.Single(results);
        Assert.Contains("'2026-08'", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-2026-08")]
    [InlineData("2026 08")]
    [InlineData("2026/08")]
    [InlineData("key\nid")]
    [InlineData("2026-08\n")]
    [InlineData("2026-08\r\n")]
    public void Validate_AnUnacceptableKeyIdentifier_IsRefused(string keyId)
    {
        // Arrange — the identifier is written into log lines and metric labels without escaping and is persisted beside
        // every value it seals, so the accepted set is narrow rather than merely careful. The trailing-newline cases are
        // why the pattern anchors on \z: `$` also matches immediately before one trailing newline, so it would admit
        // exactly the character the set exists to keep out.
        var options = RingOf((keyId, "systemd-credential:mailfathom-data-key"));
        options.ActiveKeyId = keyId;

        // Act
        var results = Validate(options);

        // Assert
        Assert.NotEmpty(results);
    }

    [Fact]
    public void Validate_AKeyConfiguringNoMaterial_IsRefused()
    {
        // Arrange
        var options = new DataEncryptionOptions { ActiveKeyId = "2026-08" };
        options.Keys.Add(new DataEncryptionKeyOptions { KeyId = "2026-08" });

        // Act
        var results = Validate(options);

        // Assert
        var result = Assert.Single(results);
        Assert.Contains("Material", result.ErrorMessage, StringComparison.Ordinal);
    }

    private static DataEncryptionOptions RingOf(params (string KeyId, string SecretReference)[] keys)
    {
        var options = new DataEncryptionOptions();

        foreach (var (keyId, secretReference) in keys)
        {
            options.Keys.Add(new DataEncryptionKeyOptions
            {
                KeyId = keyId,
                Material = new ConfiguredSecret { Name = $"data-key-{options.Keys.Count}", SecretReference = secretReference },
            });
        }

        return options;
    }

    private static IReadOnlyList<ValidationResult> Validate(DataEncryptionOptions options) =>
        [.. options.Validate(new ValidationContext(options))];
}
