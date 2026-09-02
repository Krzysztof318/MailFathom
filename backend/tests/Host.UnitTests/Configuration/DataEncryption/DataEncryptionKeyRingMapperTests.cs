// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.DataEncryption;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.DataEncryption;

/// <summary>Covers the mapping from the bound section onto the settings the encryption adapter reads.</summary>
public sealed class DataEncryptionKeyRingMapperTests
{
    [Fact]
    public void Map_AConfiguredRing_CarriesEveryKeyAndTheActiveIdentifier()
    {
        // Arrange
        var options = new DataEncryptionOptions { ActiveKeyId = "2026-08" };
        options.Keys.Add(KeyOf("2026-08", "systemd-credential:mailfathom-data-key"));
        options.Keys.Add(KeyOf("2026-02", "systemd-credential:mailfathom-data-key-previous"));

        // Act
        var settings = DataEncryptionKeyRingMapper.Map(options);

        // Assert
        Assert.Equal("2026-08", settings.ActiveKeyId);
        Assert.Equal(["2026-08", "2026-02"], settings.Keys.Select(key => key.KeyId));
        Assert.Equal(
            ["systemd-credential:mailfathom-data-key", "systemd-credential:mailfathom-data-key-previous"],
            settings.Keys.Select(key => key.Material.SecretReference));
    }

    [Fact]
    public void Map_AKeyConfiguringNoMaterial_OmitsItRatherThanCarryingAnAbsentReference()
    {
        // Arrange — startup already refuses such a snapshot, so this path is only reached while that refusal is being
        // composed. Carrying the half-built key into the ring would replace a named configuration error with a null
        // dereference at the moment the container resolves the ring.
        var options = new DataEncryptionOptions { ActiveKeyId = "2026-08" };
        options.Keys.Add(KeyOf("2026-08", "systemd-credential:mailfathom-data-key"));
        options.Keys.Add(new DataEncryptionKeyOptions { KeyId = "2026-02" });

        // Act
        var settings = DataEncryptionKeyRingMapper.Map(options);

        // Assert
        Assert.Equal(["2026-08"], settings.Keys.Select(key => key.KeyId));
    }

    [Fact]
    public void Map_AnAbsentSection_CarriesAnEmptyRing()
    {
        // Arrange — a deployment that seals nothing configures no ring, and the mapping has to survive that rather
        // than being reached only after a key exists.
        var options = new DataEncryptionOptions();

        // Act
        var settings = DataEncryptionKeyRingMapper.Map(options);

        // Assert
        Assert.Empty(settings.Keys);
        Assert.Empty(settings.ActiveKeyId);
    }

    private static DataEncryptionKeyOptions KeyOf(string keyId, string secretReference) =>
        new()
        {
            KeyId = keyId,
            Material = new ConfiguredSecret { Name = $"data-key-{keyId}", SecretReference = secretReference },
        };
}
