// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets.Discovery;

namespace MailFathom.Infrastructure.DataEncryption;

/// <summary>One configured key: the identifier stored beside every value it seals, and where its material comes from.</summary>
/// <param name="KeyId">The identifier a sealed value names its key by, which is persisted and therefore never changes.</param>
/// <param name="Material">The reference to base64 material that decodes to an AES-256 key.</param>
public sealed record DataEncryptionKeyReference(string KeyId, ConfiguredSecret Material);

/// <summary>The deployment's key ring as the encryption adapter reads it, mapped from bound configuration.</summary>
/// <param name="ActiveKeyId">The identifier of the key new values are sealed under.</param>
/// <param name="Keys">Every key a stored value may still name, including the active one.</param>
/// <remarks>
/// This is the infrastructure-facing shape of the <c>DataEncryption</c> configuration root, so the adapter depends on a
/// settings record rather than on the host's bindable options — the same split
/// <see cref="Persistence.Connections.IDatabaseConnectionSettingsValidator" /> already uses for the database
/// credential. A reloaded snapshot reaches the ring by producing a new instance of this record, so a key an operator
/// adds is available to the next operation without a restart.
/// </remarks>
public sealed record DataEncryptionKeyRingSettings(string ActiveKeyId, IReadOnlyList<DataEncryptionKeyReference> Keys);
