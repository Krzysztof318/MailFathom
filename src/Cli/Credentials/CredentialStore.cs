// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Cli.Commands;

namespace MailFathom.Cli.Credentials;

/// <summary>Where the command remembers the deployments an operator has signed in to.</summary>
/// <remarks>
/// <para>
/// This is the command's own session and nothing else. It never reads or writes the service's configuration, its
/// database, or its secret store — administering a deployment happens over HTTP, and this file exists only so that
/// signing in once is enough for the commands that follow.
/// </para>
/// <para>
/// One entry per deployment, keyed by the name the operator gave it, with one of them marked as the default. A name is
/// what an operator types and an address is what changes, so a deployment that moves port or gains a domain keeps its
/// name and its profile follows, rather than becoming a second entry nobody meant to create.
/// </para>
/// <para>
/// Tokens are sealed before they are written; see <see cref="TokenProtector" />. Opening them is this type's job rather
/// than its callers', so nothing above it ever holds a value it has to remember is still encrypted.
/// </para>
/// </remarks>
internal sealed class CredentialStore
{
    private readonly string storePath;
    private readonly TokenProtector protector;

    /// <summary>Initializes a store over an explicit file, which is what a test supplies.</summary>
    /// <param name="storePath">The file the profiles are read from and written to.</param>
    /// <param name="protector">Seals and opens the stored tokens.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal CredentialStore(string storePath, TokenProtector protector)
    {
        ArgumentNullException.ThrowIfNull(storePath);
        ArgumentNullException.ThrowIfNull(protector);

        this.storePath = storePath;
        this.protector = protector;
    }

    /// <summary>Reports where the store lives for the operator running the command.</summary>
    /// <returns>The absolute path of the credentials file.</returns>
    /// <remarks>
    /// <see cref="Environment.SpecialFolder.ApplicationData" /> resolves to <c>$XDG_CONFIG_HOME</c> or
    /// <c>~/.config</c> on Linux and to <c>%APPDATA%</c> on Windows, so one call gives the right per-user location on
    /// both without a platform branch here.
    /// </remarks>
    internal static string DefaultPath() => Path.Combine(DefaultDirectory(), "credentials.json");

    /// <summary>Reports where the key sealing the stored tokens lives.</summary>
    /// <returns>The absolute path of the key file.</returns>
    /// <remarks>Beside the store rather than inside it, so the file an operator might copy or paste into a support bundle is not the file that opens it.</remarks>
    internal static string DefaultKeyPath() => Path.Combine(DefaultDirectory(), "credentials.key");

    /// <summary>Reads every profile and which one is the default.</summary>
    /// <returns>The stored state, empty when the operator has never signed in.</returns>
    /// <exception cref="CliFailure">Thrown when the file exists but cannot be read as a credential store.</exception>
    /// <remarks>Tokens stay sealed here. Listing profiles never needs them, and a read that opened them would put every token in memory to print a table of names.</remarks>
    internal StoredCredentials Read()
    {
        if (!File.Exists(this.storePath))
        {
            return StoredCredentials.Empty();
        }

        try
        {
            using var contents = File.OpenRead(this.storePath);

            var stored = JsonSerializer.Deserialize(contents, CliJsonContext.Default.StoredCredentials);

            return stored is null
                ? StoredCredentials.Empty()

                // Rebuilt case-insensitively, because the comparer is not part of what the file records and a profile
                // an operator wrote as 'Production' has to answer to 'production'.
                : stored with
                {
                    Profiles = new Dictionary<string, StoredCredential>(
                        stored.Profiles,
                        StringComparer.OrdinalIgnoreCase),
                };
        }
        catch (Exception failure) when (failure is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new CliFailure(
                $"The credential store at {this.storePath} could not be read. Remove the file to sign in again.",
                failure);
        }
    }

    /// <summary>Settles which deployment a command acts on, and opens its token.</summary>
    /// <param name="requestedDeployment">A profile name, an absolute address, or <see langword="null" /> to use the default.</param>
    /// <returns>The profile the command acts through.</returns>
    /// <exception cref="CliFailure">Thrown when the operator is not signed in to what they named, with what to run instead.</exception>
    /// <remarks>
    /// An address and a name are accepted in the same place because they answer the same question, and nothing can
    /// confuse them: a profile name is not an absolute URI. Naming an address is what lets one invocation reach a
    /// deployment other than the default without switching to it.
    /// </remarks>
    internal SignedInProfile Resolve(string? requestedDeployment)
    {
        var (name, credential) = this.Locate(requestedDeployment);

        return new SignedInProfile(
            name,
            new Uri(credential.Endpoint, UriKind.Absolute),
            this.protector.Unprotect(credential.Token, credential.Endpoint),
            credential.Credential);
    }

    /// <summary>Settles which profile a command acts on, without opening its token.</summary>
    /// <param name="requestedDeployment">A profile name, an absolute address, or <see langword="null" /> to use the default.</param>
    /// <returns>The profile's stored name and what it holds, with the token still sealed.</returns>
    /// <exception cref="CliFailure">Thrown when the operator is not signed in to what they named, with what to run instead.</exception>
    /// <remarks>
    /// Separate from <see cref="Resolve" /> because a command that only names a profile must not depend on the sealing
    /// key: forgetting a profile whose token no longer opens is exactly what an operator needs to do about it.
    /// </remarks>
    internal (string Name, StoredCredential Credential) Locate(string? requestedDeployment)
    {
        var stored = this.Read();

        if (stored.Profiles.Count == 0)
        {
            throw new CliFailure(
                $"Not signed in. Run '{CliRootCommand.CommandName} login --endpoint https://host:port' first.");
        }

        var name = requestedDeployment is { Length: > 0 } requested
            ? MatchProfile(stored, requested)
            : stored.Default ?? throw new CliFailure(
                $"No default profile is set. Run '{CliRootCommand.CommandName} switch <name>', or name a deployment with --endpoint. {DescribeKnownProfiles(stored)}");

        if (!stored.Profiles.TryGetValue(name, out var credential))
        {
            throw new CliFailure(
                $"The default profile '{name}' is no longer in the credential store. Run '{CliRootCommand.CommandName} switch <name>'. {DescribeKnownProfiles(stored)}");
        }

        return (name, credential);
    }

    /// <summary>Remembers a deployment under a name, and makes it the default.</summary>
    /// <param name="name">The operator's name for it.</param>
    /// <param name="endpoint">The address it is served at.</param>
    /// <param name="token">The bearer credential, which is sealed before it is written.</param>
    /// <param name="credentialName">The name the deployment reported for the credential.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the store cannot be written.</exception>
    /// <remarks>Signing in makes the new profile the default, because it is the deployment the operator just chose to work with; <c>switch</c> is how that is changed without signing in again.</remarks>
    internal void Save(string name, Uri endpoint, string token, string credentialName)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(credentialName);

        var stored = this.Read();
        var address = AddressOf(endpoint);

        stored.Profiles[name] = new StoredCredential(address, this.protector.Protect(token, address), credentialName);

        this.Write(stored with { Default = name });
    }

    /// <summary>Forgets one profile.</summary>
    /// <param name="name">The profile name.</param>
    /// <returns><see langword="true" /> when a profile was removed, <see langword="false" /> when no profile carried that name.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name" /> is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the store cannot be written.</exception>
    /// <remarks>Removing the default leaves the remaining profiles without one rather than promoting an arbitrary neighbour, so the next command says which deployment it needs instead of quietly choosing a different one.</remarks>
    internal bool Remove(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var stored = this.Read();

        if (!stored.Profiles.Remove(name))
        {
            return false;
        }

        this.Write(string.Equals(stored.Default, name, StringComparison.OrdinalIgnoreCase)
            ? stored with { Default = null }
            : stored);

        return true;
    }

    /// <summary>Makes one profile the default for later commands.</summary>
    /// <param name="name">The profile name.</param>
    /// <returns>The profile's stored name, which is the one it was created with rather than the spelling just typed, and what it holds.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name" /> is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when no profile carries that name.</exception>
    internal (string Name, StoredCredential Credential) SwitchTo(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var stored = this.Read();
        var matched = MatchProfile(stored, name);

        this.Write(stored with { Default = matched });

        return (matched, stored.Profiles[matched]);
    }

    /// <summary>Names the profile an operator asked for, whether they wrote its name or its address.</summary>
    /// <remarks>The address form compares authorities, so <c>https://Host:8443/</c> and <c>https://host:8443</c> find the same profile rather than one of them finding nothing.</remarks>
    private static string MatchProfile(StoredCredentials stored, string requested)
    {
        // The stored spelling rather than the typed one, because this name is what every later message prints and what
        // the default is written as. Returning what was typed would let 'switch PRODUCTION' rewrite the file's idea of
        // the profile's name without renaming the profile.
        foreach (var storedName in stored.Profiles.Keys)
        {
            if (string.Equals(storedName, requested, StringComparison.OrdinalIgnoreCase))
            {
                return storedName;
            }
        }

        if (Uri.TryCreate(requested.Trim(), UriKind.Absolute, out var address)
            && (address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp))
        {
            var authority = AddressOf(address);

            foreach (var profile in stored.Profiles)
            {
                if (string.Equals(profile.Value.Endpoint, authority, StringComparison.OrdinalIgnoreCase))
                {
                    return profile.Key;
                }
            }

            throw new CliFailure(
                $"Not signed in to {authority}. Run '{CliRootCommand.CommandName} login --endpoint {authority}'. {DescribeKnownProfiles(stored)}");
        }

        throw new CliFailure(
            $"There is no profile named '{requested}'. {DescribeKnownProfiles(stored)}");
    }

    private static string DescribeKnownProfiles(StoredCredentials stored) => stored.Profiles.Count == 0
        ? $"No deployment has been signed in to yet; run '{CliRootCommand.CommandName} login --endpoint https://host:port'."
        : $"Signed in: {string.Join(", ", stored.Profiles.Keys.Order(StringComparer.OrdinalIgnoreCase))}.";

    /// <summary>Reduces an endpoint to what identifies the deployment.</summary>
    /// <remarks>The authority without a trailing slash, lowercased by the URI parser, so two spellings of one address are one profile rather than two — and so the value bound into the sealed token is stable across them.</remarks>
    private static string AddressOf(Uri endpoint) => endpoint.GetLeftPart(UriPartial.Authority);

    private static string DefaultDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "MailFathom");

    private void Write(StoredCredentials credentials)
    {
        try
        {
            OwnerOnlyStorage.CreateDirectoryFor(this.storePath);

            using var contents = OwnerOnlyStorage.OpenForWriting(this.storePath);

            JsonSerializer.Serialize(contents, credentials, CliJsonContext.Default.StoredCredentials);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            throw new CliFailure($"The credential store at {this.storePath} could not be written.", failure);
        }
    }
}
