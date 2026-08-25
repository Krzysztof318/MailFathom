// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Credentials.SecretStores;

namespace MailFathom.Cli.UnitTests;

/// <summary>A secret store a test can hold in its hand, standing in for the platform's own.</summary>
/// <remarks>
/// Neither real implementation is reachable from a unit test: one needs a Windows logon session and the other a D-Bus
/// session bus and a running keyring, and both would leave entries on the machine that ran the suite. What is worth
/// asserting is not either platform call anyway — it is what the credential store does with a store that answers, one
/// that refuses, and one that has forgotten an entry, which is exactly what this makes reachable.
/// </remarks>
internal sealed class FakeOperatorSecretStore : IOperatorSecretStore
{
    private readonly Dictionary<string, string> entries = new(StringComparer.Ordinal);

    /// <summary>Gets or sets why this machine has no store, or <see langword="null" /> while it has one.</summary>
    /// <remarks>Settable so that one test can sign in against a store and then take it away, which is the shape of a keyring that has been locked or a session that has ended between two commands.</remarks>
    internal string? Refusal { get; set; }

    /// <summary>Gets what the store is holding, keyed as <see cref="KeyOf" /> spells it.</summary>
    internal IReadOnlyDictionary<string, string> Entries => this.entries;

    /// <inheritdoc />
    public string Description => "the platform's secret store";

    /// <inheritdoc />
    public string? Read(ProfileSecret secret)
    {
        this.RefuseWhenAsked();

        return this.entries.GetValueOrDefault(KeyOf(secret));
    }

    /// <inheritdoc />
    public void Write(ProfileSecret secret, string value)
    {
        this.RefuseWhenAsked();

        this.entries[KeyOf(secret)] = value;
    }

    /// <inheritdoc />
    public bool Clear(ProfileSecret secret)
    {
        this.RefuseWhenAsked();

        return this.entries.Remove(KeyOf(secret));
    }

    /// <summary>Spells one entry's key the way a test asserts on it.</summary>
    internal static string KeyOf(ProfileSecret secret) => $"{secret.Address}/{secret.Kind}";

    private void RefuseWhenAsked()
    {
        if (this.Refusal is { } reason)
        {
            throw new SecretStoreUnavailable(reason);
        }
    }
}
