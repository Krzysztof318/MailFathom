// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.DataEncryption;

/// <summary>A value as it is stored: the ciphertext, and the identifier of the key that sealed it.</summary>
/// <param name="KeyId">The key the value was sealed under, stored beside it.</param>
/// <param name="Ciphertext">The sealed bytes, in the layout <see cref="Common.AesGcmEnvelope" /> fixes.</param>
/// <remarks>
/// <para>
/// Storing the key identifier beside the ciphertext is what makes rotation possible at all. Without it, replacing a key
/// would be a flag day with the service stopped, because nothing could tell which of two keys opens a given row. With
/// it, two keys coexist, a value is re-sealed under the active key the next time it is written, and a key is retired
/// once nothing references it.
/// </para>
/// <para>
/// The identifier is not a secret and is not authentication on its own: it is authenticated into the ciphertext through
/// the binding, so rewriting it in the database makes the value fail to open rather than making it open under another
/// key.
/// </para>
/// </remarks>
public sealed record SealedValue(string KeyId, ReadOnlyMemory<byte> Ciphertext);
