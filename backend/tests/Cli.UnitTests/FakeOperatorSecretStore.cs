// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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

    private readonly Dictionary<string, string> refusedSecrets = new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> refusedClears = new(StringComparer.Ordinal);

    /// <summary>Gets or sets why this machine has no store, or <see langword="null" /> while it has one.</summary>
    /// <remarks>Settable so that one test can sign in against a store and then take it away, which is the shape of a keyring that has been locked or a session that has ended between two commands.</remarks>
    internal string? Refusal { get; set; }

    /// <summary>Gets what the store is holding, keyed as <see cref="KeyOf" /> spells it.</summary>
    internal IReadOnlyDictionary<string, string> Entries => this.entries;

    /// <summary>Refuses to take one secret while the rest of the store goes on answering.</summary>
    /// <param name="secret">Which secret the store will not take.</param>
    /// <param name="reason">What the refusal says.</param>
    /// <remarks>
    /// A whole-store <see cref="Refusal" /> cannot arrange the one input that reaches the withdrawal in
    /// <c>CredentialStore.Place</c>: a store that takes the access token and then refuses the refresh token, which is
    /// what a blob over the Credential Manager's size limit looks like on Windows. Without it that branch is
    /// unreachable from a test, and it is the branch that decides whether a live credential is left in an operator's
    /// keyring under a profile whose file says it holds none.
    /// <para>
    /// The write alone, because that is what the failure it stands for refuses: an entry the store would not accept is
    /// still one the store will hand back and let go of. A collection that has locked refuses everything and is
    /// <see cref="Refusal" />; a store that will not release an entry it holds is <see cref="RefuseClearing" />.
    /// </para>
    /// </remarks>
    internal void RefuseWriting(ProfileSecret secret, string reason) => this.refusedSecrets[KeyOf(secret)] = reason;

    /// <summary>Keeps one secret readable and writable while refusing to let it go.</summary>
    /// <param name="secret">Which secret the store will not remove.</param>
    /// <param name="reason">What the refusal says.</param>
    /// <remarks>
    /// A collection that locks between a sign-in's two writes refuses the second write and the withdrawal of the first
    /// for the same reason, which is the one arrangement in which the credential store has to report an entry it left
    /// behind rather than a rollback that quietly worked. <see cref="RefuseWriting" /> cannot express it: it refuses
    /// the write alone, and a whole-store <see cref="Refusal" /> would stop the write that has to succeed first.
    /// </remarks>
    internal void RefuseClearing(ProfileSecret secret, string reason) => this.refusedClears[KeyOf(secret)] = reason;

    /// <inheritdoc />
    public string Description => "the platform's secret store";

    /// <inheritdoc />
    public string? Read(ProfileSecret secret)
    {
        this.RefuseWhileTheStoreIsGone();

        return this.entries.GetValueOrDefault(KeyOf(secret));
    }

    /// <inheritdoc />
    public void Write(ProfileSecret secret, string value)
    {
        this.RefuseWhileTheStoreIsGone();
        Refuse(this.refusedSecrets, secret);

        this.entries[KeyOf(secret)] = value;
    }

    /// <inheritdoc />
    public bool Clear(ProfileSecret secret)
    {
        this.RefuseWhileTheStoreIsGone();
        Refuse(this.refusedClears, secret);

        return this.entries.Remove(KeyOf(secret));
    }

    /// <summary>Spells one entry's key the way a test asserts on it.</summary>
    internal static string KeyOf(ProfileSecret secret) =>
        $"{secret.Address}/{secret.Profile}/{secret.Kind}";

    private static void Refuse(Dictionary<string, string> refusals, ProfileSecret secret)
    {
        if (refusals.GetValueOrDefault(KeyOf(secret)) is { } reason)
        {
            throw new SecretStoreUnavailable(reason);
        }
    }

    private void RefuseWhileTheStoreIsGone()
    {
        if (this.Refusal is { } reason)
        {
            throw new SecretStoreUnavailable(reason);
        }
    }
}
