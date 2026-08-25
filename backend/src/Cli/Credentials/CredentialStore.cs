// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Cli.Commands;
using MailFathom.Cli.Credentials.SecretStores;

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
/// <b>The secrets go where the operating system holds one for a person.</b> Where this machine has such a store, the
/// file records the address, the credential's name, a key-pair path, the session metadata, and the transport trust —
/// and no secret at all. Where it does not, the tokens are sealed into the file instead; see
/// <see cref="TokenProtector" /> for what that is worth and <see cref="IOperatorSecretStore" /> for what it is not.
/// Which of the two happened is returned rather than assumed, because the weaker arrangement is one an operator has to
/// be told they got.
/// </para>
/// <para>
/// Opening a secret is this type's job rather than its callers', so nothing above it ever holds a value it has to
/// remember is still sealed, or has to know which of the two places it came out of.
/// </para>
/// </remarks>
internal sealed class CredentialStore
{
    private readonly string storePath;
    private readonly TokenProtector protector;
    private readonly IOperatorSecretStore secretStore;

    /// <summary>Initializes a store over an explicit file, which is what a test supplies.</summary>
    /// <param name="storePath">The file the profiles are read from and written to.</param>
    /// <param name="protector">Seals and opens the tokens of a profile no secret store took.</param>
    /// <param name="secretStore">The platform store the secrets belong in, or <see langword="null" /> for a machine with none.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument other than <paramref name="secretStore" /> is <see langword="null" />.</exception>
    internal CredentialStore(string storePath, TokenProtector protector, IOperatorSecretStore? secretStore = null)
    {
        ArgumentNullException.ThrowIfNull(storePath);
        ArgumentNullException.ThrowIfNull(protector);

        this.storePath = storePath;
        this.protector = protector;
        this.secretStore = secretStore ?? NoSecretStore.Instance;
    }

    /// <summary>Reports where the store lives for the operator running the command.</summary>
    /// <returns>The absolute path of the credentials file.</returns>
    /// <remarks><see cref="OperatorDirectory" /> holds where that is on each platform, and why everything the command owns on a machine lives in one directory.</remarks>
    internal static string DefaultPath() => Path.Combine(OperatorDirectory.Resolve(), "credentials.json");

    /// <summary>Reports where the key sealing a profile no secret store took lives.</summary>
    /// <returns>The absolute path of the key file.</returns>
    /// <remarks>Beside the store rather than inside it, so the file an operator might copy or paste into a support bundle is not the file that opens it. It is created on first use and removed again once no profile is sealed under it.</remarks>
    internal static string DefaultKeyPath() => Path.Combine(OperatorDirectory.Resolve(), "credentials.key");

    /// <summary>Reads every profile and which one is the default.</summary>
    /// <returns>The stored state, empty when the operator has never signed in.</returns>
    /// <exception cref="CliFailure">Thrown when the file exists but cannot be read as a credential store.</exception>
    /// <remarks>Secrets stay where they are here. Listing profiles never needs them, and a read that opened them would put every token in memory to print a table of names.</remarks>
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

    /// <summary>Settles which deployment a command acts on, and opens its secrets.</summary>
    /// <param name="requestedDeployment">A profile name, an absolute address, or <see langword="null" /> to use the default.</param>
    /// <returns>The profile the command acts through.</returns>
    /// <exception cref="CliFailure">Thrown when the operator is not signed in to what they named, with what to run instead, and when a secret the platform store holds cannot be read back.</exception>
    /// <remarks>
    /// <para>
    /// An address and a name are accepted in the same place because they answer the same question, and nothing can
    /// confuse them: a profile name is not an absolute URI. Naming an address is what lets one invocation reach a
    /// deployment other than the default without switching to it.
    /// </para>
    /// <para>
    /// This is also where a profile written before there was a secret store moves into one. It happens on the first
    /// command that opens the profile rather than at a sign-in, so an upgrade costs the operator nothing; a move that
    /// does not complete leaves the sealed profile exactly as it was, and the command it happened under goes on.
    /// </para>
    /// </remarks>
    internal SignedInProfile Resolve(string? requestedDeployment)
    {
        var (name, credential) = this.Locate(requestedDeployment);

        // A key-pair profile holds no credential in either place: every command mints its own assertion, and
        // DeploymentAccess is what fills this in. Read first so neither storage is consulted for a secret that does
        // not exist, including the sealed empty token an older command wrote for such a profile.
        var token = credential.KeyPair is not null
            ? string.Empty
            : this.Open(credential.Token, ProfileSecret.BearerToken(name, credential.Endpoint), credential.Endpoint);

        var session = this.OpenSession(name, credential);

        var uncleared = this.MoveIntoSecretStore(name, credential, token, session);

        return new SignedInProfile(
            name,
            new Uri(credential.Endpoint, UriKind.Absolute),
            token,
            credential.Credential,
            session,
            credential.KeyPair)
        {
            Trust = credential.Transport ?? StoredTransportTrust.Protected,
            Uncleared = uncleared,
        };
    }

    /// <summary>Settles which profile a command acts on, without opening its secrets.</summary>
    /// <param name="requestedDeployment">A profile name, an absolute address, or <see langword="null" /> to use the default.</param>
    /// <returns>The profile's stored name and what it holds, with nothing opened.</returns>
    /// <exception cref="CliFailure">Thrown when the operator is not signed in to what they named, with what to run instead.</exception>
    /// <remarks>
    /// Separate from <see cref="Resolve" /> because a command that only names a profile must not depend on the sealing
    /// key or on a keyring being open: forgetting a profile whose token no longer opens is exactly what an operator
    /// needs to do about it.
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
    /// <param name="token">The bearer credential.</param>
    /// <param name="credentialName">The name the deployment reported for the credential.</param>
    /// <param name="session">What an OAuth sign-in left behind, whose refresh token is kept alongside the access token, or <see langword="null" /> for a presented credential.</param>
    /// <param name="keyPair">Where a key-pair profile's private key lives, or <see langword="null" /> when the profile stores a credential of its own.</param>
    /// <param name="trust">What the operator accepted about this deployment's transport, or <see langword="null" /> when they accepted nothing beyond the default.</param>
    /// <returns>The name the profile is filed under, which is the one an earlier sign-in chose when the two spellings differ, and which of the two places took its secrets, for the command to say both.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument other than <paramref name="session" />, <paramref name="keyPair" />, or <paramref name="trust" /> is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the store cannot be written.</exception>
    /// <remarks>
    /// Signing in makes the new profile the default, because it is the deployment the operator just chose to work with;
    /// <c>switch</c> is how that is changed without signing in again.
    /// <para>
    /// A key-pair profile stores no secret in either place, and signing one in removes whatever an earlier profile at
    /// the same address left in the platform store. The file then holds a path and no credential, which is the whole
    /// point of that way of signing in.
    /// </para>
    /// </remarks>
    internal (string Name, SecretPlacement Placement) Save(
        string name,
        Uri endpoint,
        string token,
        string credentialName,
        OAuthSession? session = null,
        StoredKeyPair? keyPair = null,
        StoredTransportTrust? trust = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(credentialName);

        var stored = this.Read();
        var address = AddressOf(endpoint);

        // The spelling the file already files this profile under, because that is what every read afterwards uses:
        // the profiles are keyed without regard to case, so signing in as 'PRODUCTION' over 'Production' rewrites the
        // value and keeps the key, and a secret keyed by what was typed would be filed where nothing looks.
        var profileName = StoredNameFor(stored, name) ?? name;

        // What the platform store is already holding for this profile, which is what decides both whether an entry
        // that will not clear is an orphan worth reporting and what has to be removed once the new record is durable.
        // A profile keeps its name when its deployment moves port or gains a domain, so the entries it leaves at the
        // address it is moving off are reachable from nowhere afterwards: not from the new placement, and not from the
        // logout that reads the profile's current address.
        var replaced = stored.Profiles.GetValueOrDefault(profileName) is { KeyPair: null, Token: null } previous
            ? previous
            : null;

        var heldBefore = replaced is not null
            && string.Equals(replaced.Endpoint, address, StringComparison.OrdinalIgnoreCase);

        var abandonedAddress = heldBefore ? null : replaced?.Endpoint;

        var placement = keyPair is null
            ? this.Place(profileName, address, token, session?.RefreshToken, heldBefore)
            : SecretPlacement.NothingToKeep;

        var held = placement.Store is not null;

        stored.Profiles[profileName] = new StoredCredential(
            address,
            keyPair is not null || held ? null : this.protector.Protect(token, address),
            credentialName,
            this.Record(session, address, held),
            keyPair,
            Recorded(trust));

        var written = stored with { Default = profileName };

        this.WriteOrWithdraw(written, profileName, address, heldBefore);

        // After the write and never before it. Each of these removes an entry the record that was in the file until
        // this moment pointed at, so a write that then failed would take a working profile with it: the file would
        // still name the old address, or still say the store holds this profile, with nothing left under either key.
        var leftBehind = this.ForgetWhatNothingPointsAtAnyMore(
            profileName,
            keyPair is null ? null : address,
            abandonedAddress,
            heldBefore);

        this.DiscardKeyIfUnused(written);

        return (profileName, placement with { Uncleared = placement.Uncleared ?? leftBehind });
    }

    /// <summary>Writes the store, taking the placement back out when the file it was made for was not written.</summary>
    /// <remarks>
    /// A placement is made before the file is, so a directory that is full or read-only leaves a live credential in the
    /// keyring under a profile name the file never gained: <c>Resolve</c> asks only for profiles the file carries, and
    /// <c>logout</c> can name only those too, so nothing would ever read it and nothing would ever remove it.
    /// <para>
    /// Not where the store was already holding this profile's secrets. The file then still describes a profile whose
    /// credential is in the store, and withdrawing would break the profile that survived the failure instead of
    /// cleaning up after the one that did not happen — the entries under that key are this sign-in's, which is a
    /// credential the surviving record can present rather than one nothing points at.
    /// </para>
    /// </remarks>
    private void WriteOrWithdraw(StoredCredentials written, string profile, string address, bool heldBefore)
    {
        try
        {
            this.Write(written);
        }
        catch (CliFailure unwritten) when (!heldBefore)
        {
            if (this.ForgetBoth(profile, address) is not { } refusal)
            {
                throw;
            }

            // The sign-in is failing either way, so this is the one chance to say that the credential reached the
            // store and could not be taken back out; the file failure alone would send the operator to their disk.
            throw new CliFailure(
                $"{unwritten.Message} The credential had already been placed in the platform's secret store and could not be taken back out ({refusal}), so remove this profile's entries from your keyring.",
                unwritten);
        }
    }

    /// <summary>Replaces one profile's access token with the one a silent renewal produced.</summary>
    /// <param name="name">The profile's stored name.</param>
    /// <param name="accessToken">The freshly issued access token.</param>
    /// <param name="accessTokenExpiresAt">When the new token stops being accepted.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name" /> or <paramref name="accessToken" /> is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the store cannot be written.</exception>
    /// <remarks>
    /// The refresh token, the endpoint, and which profile is the default are all left exactly as they were. A renewal is
    /// not a sign-in: it produces one new value and must not quietly move the session's end, adopt a rotated refresh
    /// token, or change which deployment later commands act on. A profile that has been forgotten in between is left
    /// forgotten rather than recreated by the renewal that was already in flight.
    /// <para>
    /// A renewal whose platform store has gone away since the sign-in changes nothing at all rather than falling back
    /// to the file: the profile's other secret is still in that store, and moving half of it out would leave a session
    /// no arrangement can open. The next command renews again.
    /// </para>
    /// </remarks>
    internal void RenewAccessToken(string name, string accessToken, DateTimeOffset accessTokenExpiresAt)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(accessToken);

        var stored = this.Read();

        if (!stored.Profiles.TryGetValue(name, out var credential) || credential.Session is not { } session)
        {
            return;
        }

        if (credential.Token is null)
        {
            try
            {
                this.secretStore.Write(ProfileSecret.BearerToken(name, credential.Endpoint), accessToken);
            }
            catch (SecretStoreUnavailable)
            {
                return;
            }

            stored.Profiles[name] = credential with
            {
                Session = session with { AccessTokenExpiresAt = accessTokenExpiresAt },
            };
        }
        else
        {
            stored.Profiles[name] = credential with
            {
                Token = this.protector.Protect(accessToken, credential.Endpoint),
                Session = session with { AccessTokenExpiresAt = accessTokenExpiresAt },
            };
        }

        this.Write(stored);
    }

    /// <summary>Forgets one profile, in both places it may keep something.</summary>
    /// <param name="name">The profile name.</param>
    /// <returns>Whether a profile carried that name, and what could not be cleared from the platform store.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name" /> is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the store cannot be written.</exception>
    /// <remarks>Removing the default leaves the remaining profiles without one rather than promoting an arbitrary neighbour, so the next command says which deployment it needs instead of quietly choosing a different one.</remarks>
    internal ProfileRemoval Remove(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var stored = this.Read();

        if (!stored.Profiles.Remove(name, out var credential))
        {
            return ProfileRemoval.NothingToForget;
        }

        var remaining = string.Equals(stored.Default, name, StringComparison.OrdinalIgnoreCase)
            ? stored with { Default = null }
            : stored;

        this.Write(remaining);
        this.DiscardKeyIfUnused(remaining);

        return new ProfileRemoval(Removed: true, this.ClearSecrets(name, credential));
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
        if (StoredNameFor(stored, requested) is { } matched)
        {
            return matched;
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

    /// <summary>Names the key the file already files a profile under, whatever spelling was typed.</summary>
    /// <remarks>
    /// The profiles are keyed without regard to case, so writing under a second spelling replaces the value and keeps
    /// the original key — and every read afterwards goes through the stored one. A secret keyed by the typed spelling
    /// would therefore be filed where nothing will ever look for it, while the entries the previous sign-in left under
    /// the stored one stay in the operator's keyring with nothing left that reaches them.
    /// </remarks>
    private static string? StoredNameFor(StoredCredentials stored, string name) => stored.Profiles.Keys
        .FirstOrDefault(storedName => string.Equals(storedName, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Reports what a profile has to record about its transport, which is nothing when it accepted nothing.</summary>
    /// <remarks>Leaving the default out keeps the file of an ordinary HTTPS profile exactly as it was, so the presence of the member is itself the statement that something was accepted.</remarks>
    private static StoredTransportTrust? Recorded(StoredTransportTrust? trust) =>
        trust is null || trust == StoredTransportTrust.Protected ? null : trust;

    /// <summary>Reduces an endpoint to what identifies the deployment.</summary>
    /// <remarks>The authority without a trailing slash, lowercased by the URI parser, so two spellings of one address are one profile rather than two — and so the value the secrets are keyed by is stable across them.</remarks>
    private static string AddressOf(Uri endpoint) => endpoint.GetLeftPart(UriPartial.Authority);

    /// <summary>Hands a profile's secrets to the platform store, or reports why they have to be sealed instead.</summary>
    /// <remarks>
    /// <para>
    /// Both or neither. A store that took the access token and refused the refresh token would leave a session split
    /// between two places, so the first entry is taken back out and the profile is sealed whole — one arrangement per
    /// profile is what makes reading one back a single decision.
    /// </para>
    /// <para>
    /// The taking back is the one step that can itself fail, because the ordinary way the second write refuses is a
    /// collection locking mid-command and a locked collection will not give the first entry up either. That leaves a
    /// live credential in the operator's keyring under a profile whose file entry says it is sealed, which
    /// <see cref="ClearSecrets" /> would then pass over at <c>logout</c> — so it is carried out with the placement and
    /// said, rather than swallowed as an unlucky rollback.
    /// </para>
    /// </remarks>
    private SecretPlacement Place(string profile, string address, string token, string? refreshToken, bool heldBefore)
    {
        try
        {
            this.secretStore.Write(ProfileSecret.BearerToken(profile, address), token);
        }
        catch (SecretStoreUnavailable unavailable)
        {
            // A profile the store was already holding gains a sealed token in the file from here, and ClearSecrets
            // passes over a sealed profile — so the entries it still holds would be reachable from nothing at all.
            return SecretPlacement.Sealed(
                unavailable.Message,
                Reportable(unavailable.Message, heldBefore));
        }

        if (refreshToken is null)
        {
            // Signing in with an API key over a profile that used to hold an OAuth session leaves that session's
            // refresh token behind otherwise, which is a credential nothing will ever present again.
            return SecretPlacement.Held(
                this.secretStore.Description,
                Reportable(this.Forget(ProfileSecret.RefreshToken(profile, address)), heldBefore));
        }

        try
        {
            this.secretStore.Write(ProfileSecret.RefreshToken(profile, address), refreshToken);
        }
        catch (SecretStoreUnavailable unavailable)
        {
            // Both, because the refresh entry a previous sign-in left is still there and this profile is about to be
            // sealed whole — after which nothing looks at either key again. Always reported: the write above succeeded
            // against this same store moments ago, so an entry the withdrawal could not reach demonstrably exists.
            return SecretPlacement.Sealed(unavailable.Message, this.ForgetBoth(profile, address));
        }

        return SecretPlacement.Held(this.secretStore.Description);
    }

    /// <summary>Removes what the profile the file now records is no longer reachable through.</summary>
    /// <param name="profile">The profile's stored name.</param>
    /// <param name="keptNothingAt">The profile's own address when this sign-in stores no secret at all, or <see langword="null" /> when it stores one there.</param>
    /// <param name="abandonedAddress">The address the profile has just moved off, or <see langword="null" /> when it moved nowhere.</param>
    /// <param name="heldBefore">Whether the store was holding this profile's secrets at its own address before this sign-in.</param>
    /// <returns>The first refusal, or <see langword="null" /> when everything that had to go went.</returns>
    /// <remarks>
    /// Two cases, and both are a removal rather than a replacement, which is why neither can run before the file is
    /// written. Signing a key-pair profile in over one whose tokens the store held leaves those tokens with nothing to
    /// present them, and a deployment that moved leaves a pair under an address nothing will ask about again.
    /// <para>
    /// The first is reported only where the store really was holding something, since a machine that never had one
    /// would otherwise be told about an orphan that cannot exist; the second always is, because a profile reaches it
    /// only by having been store-held at that other address.
    /// </para>
    /// </remarks>
    private string? ForgetWhatNothingPointsAtAnyMore(
        string profile,
        string? keptNothingAt,
        string? abandonedAddress,
        bool heldBefore)
    {
        var here = keptNothingAt is null ? null : Reportable(this.ForgetBoth(profile, keptNothingAt), heldBefore);
        var moved = abandonedAddress is null ? null : this.ForgetBoth(profile, abandonedAddress);

        return here ?? moved;
    }

    /// <summary>Takes both of a profile's secrets out of the platform store, and reports the first refusal.</summary>
    /// <remarks>
    /// Both are attempted whatever the first one answers. A refusal here can be about one entry rather than about the
    /// store — the Credential Manager refuses a delete per target — and on every path that reaches this the profile is
    /// being replaced or forgotten, so nothing comes back for the second entry later. Leaving the refresh token, which
    /// is the longer-lived of the two, on the strength of the bearer token's refusal would be exactly that.
    /// </remarks>
    private string? ForgetBoth(string profile, string address)
    {
        var bearer = this.Forget(ProfileSecret.BearerToken(profile, address));
        var refresh = this.Forget(ProfileSecret.RefreshToken(profile, address));

        return bearer ?? refresh;
    }

    /// <summary>Removes one entry where a store answers, and reports a store that would not let it go.</summary>
    private string? Forget(ProfileSecret secret)
    {
        try
        {
            this.secretStore.Clear(secret);

            return null;
        }
        catch (SecretStoreUnavailable unavailable)
        {
            return unavailable.Message;
        }
    }

    /// <summary>Keeps a refusal only where the profile had something in the store for it to be about.</summary>
    /// <remarks>Every clear on a machine with no store at all refuses, and there is nothing on such a machine to be left behind — so reporting one would put a warning about an orphaned credential in front of every operator who signs in on a headless host, which is the population least able to check.</remarks>
    private static string? Reportable(string? refusal, bool heldBefore) => heldBefore ? refusal : null;

    /// <summary>Records an OAuth session, sealing its refresh token only where the platform store did not take it.</summary>
    private StoredOAuthSession? Record(OAuthSession? session, string address, bool held) => session is null
        ? null
        : new StoredOAuthSession(
            held ? null : this.protector.Protect(session.RefreshToken, address),
            session.AccessTokenExpiresAt,
            session.TokenEndpoint.ToString(),
            session.Issuer,
            session.ClientId,
            session.Resource,
            session.Scope);

    /// <summary>Reads one secret back from whichever of the two places the profile keeps it in.</summary>
    /// <remarks>
    /// A profile the platform store holds has no fallback, and says so rather than reporting a corrupt file: the
    /// keyring being locked and the entry having been removed are different things to an operator, and only one of them
    /// is answered by signing in again.
    /// </remarks>
    private string Open(string? sealedSecret, ProfileSecret secret, string address)
    {
        if (sealedSecret is not null)
        {
            return this.protector.Unprotect(sealedSecret, address);
        }

        string? held;

        try
        {
            held = this.secretStore.Read(secret);
        }
        catch (SecretStoreUnavailable unavailable)
        {
            throw new CliFailure(
                $"The credential for {address} is held by this machine's secret store, which cannot be reached: {unavailable.Message}. Make the store reachable and run the command again, or run '{CliRootCommand.CommandName} login --endpoint {address}' to store the credential wherever this machine can hold it.",
                unavailable);
        }

        return held ?? throw new CliFailure(
            $"{this.secretStore.Description} no longer holds the credential for {address}. Run '{CliRootCommand.CommandName} login --endpoint {address}' to store it again.");
    }

    /// <summary>Opens a stored OAuth session, or reports none where the profile holds an API key.</summary>
    /// <remarks>
    /// A session whose token endpoint is no longer an absolute address is read as no session at all, so the profile
    /// still authenticates with the access token it holds until that expires. The alternative would be failing every
    /// command against a store an older or hand-edited file left in that state, which is a worse answer than one
    /// sign-in.
    /// </remarks>
    private OAuthSession? OpenSession(string name, StoredCredential credential) =>
        credential.Session is { } session
        && Uri.TryCreate(session.TokenEndpoint, UriKind.Absolute, out var tokenEndpoint)
            ? new OAuthSession(
                this.Open(session.RefreshToken, ProfileSecret.RefreshToken(name, credential.Endpoint), credential.Endpoint),
                session.AccessTokenExpiresAt,
                tokenEndpoint,
                session.Issuer,
                session.ClientId,
                session.Resource,
                session.Scope)
            : null;

    /// <summary>Moves a profile written before this machine had a secret store into the one it has now.</summary>
    /// <remarks>
    /// <para>
    /// On the first command that opens the profile, because that is the one moment both secrets are already in hand:
    /// doing it at a sign-in would mean asking for a credential the operator has, and doing it eagerly would mean
    /// opening every profile's tokens to migrate the one being used.
    /// </para>
    /// <para>
    /// The store is written first and the file second, so an interruption leaves the sealed values still readable and
    /// the entries merely duplicated — the next command overwrites them. A file that cannot be rewritten is passed over
    /// entirely rather than failing the command the operator actually ran.
    /// </para>
    /// </remarks>
    private string? MoveIntoSecretStore(string name, StoredCredential credential, string token, OAuthSession? session)
    {
        if (credential.Token is null && credential.Session?.RefreshToken is null)
        {
            return null;
        }

        if (credential.KeyPair is not null)
        {
            // No store is involved: such a profile keeps nothing anywhere, and the sealed empty token an older command
            // wrote for it is the only thing keeping a key file alive.
            this.Rewrite(name, stored => stored with { Token = null });

            return null;
        }

        // Nothing of this profile is in the store yet — that is what makes it a profile to move — so a clear that
        // refuses on the way is about an entry that was never there.
        var placement = this.Place(name, credential.Endpoint, token, session?.RefreshToken, heldBefore: false);

        if (placement.Store is null)
        {
            // A move that took the first secret and could not put it back has left one where the file entry, still
            // sealed, says there is none — so ClearSecrets will pass over it at every later logout. Carried out rather
            // than dropped, because the command this happened under is the only one that will ever know.
            return placement.Uncleared;
        }

        this.Rewrite(name, stored => stored with
        {
            Token = null,
            Session = stored.Session is { } recorded ? recorded with { RefreshToken = null } : null,
        });

        return null;
    }

    /// <summary>Replaces one profile in the file, leaving a store somebody else has changed in the meantime alone.</summary>
    /// <remarks>Failure is swallowed because every caller is an upgrade rather than something the operator asked for: what is on disk still opens, and reporting a write that nobody requested would turn a working command into a failed one.</remarks>
    private void Rewrite(string name, Func<StoredCredential, StoredCredential> change)
    {
        try
        {
            var stored = this.Read();

            if (!stored.Profiles.TryGetValue(name, out var credential))
            {
                return;
            }

            stored.Profiles[name] = change(credential);

            this.Write(stored);
            this.DiscardKeyIfUnused(stored);
        }
        catch (CliFailure)
        {
        }
    }

    /// <summary>Clears a forgotten profile's entries from the platform store, and says what would not go.</summary>
    /// <remarks>
    /// Nothing is attempted for a profile whose secrets were never in the store, so a machine with no keyring reports
    /// nothing rather than a refusal it was always going to get. A second profile at the same deployment keeps its own
    /// entries untouched, because an entry names the profile that holds it as well as the deployment it reaches.
    /// </remarks>
    private string? ClearSecrets(string name, StoredCredential credential)
    {
        if (credential.KeyPair is not null || credential.Token is not null)
        {
            return null;
        }

        return this.ForgetBoth(name, credential.Endpoint);
    }

    /// <summary>Removes the sealing key once no profile is sealed under it.</summary>
    /// <remarks>The key is the weaker of the two arrangements, so leaving it behind on a machine whose profiles have all moved into the platform store would keep material alive that protects nothing. It is recreated on first use if a later sign-in has to seal something again.</remarks>
    private void DiscardKeyIfUnused(StoredCredentials stored)
    {
        var stillSealed = stored.Profiles.Values.Any(
            profile => profile.Token is not null || profile.Session?.RefreshToken is not null);

        if (!stillSealed)
        {
            this.protector.Discard();
        }
    }

    /// <summary>Replaces the store with a new one, in a way an interrupted process cannot leave half written.</summary>
    /// <remarks>
    /// Written beside the store and renamed over it, because the rename is the only step that changes what a later
    /// command reads and a rename either happened or did not. Serializing over the file itself would truncate it first,
    /// so a process that stopped in between would leave every profile in it unreadable — and the write is reached by
    /// <see cref="RenewAccessToken" /> from what an operator ran as a status check, which is not a command anybody
    /// would think to run twice. The renamed file carries the mode it was created with, so the store stays readable by
    /// its owner alone without a second call to widen and re-tighten it.
    /// </remarks>
    private void Write(StoredCredentials credentials)
    {
        var pending = this.storePath + ".pending";

        try
        {
            OwnerOnlyStorage.CreateDirectoryFor(this.storePath);

            using (var contents = OwnerOnlyStorage.OpenForWriting(pending))
            {
                JsonSerializer.Serialize(contents, credentials, CliJsonContext.Default.StoredCredentials);
            }

            File.Move(pending, this.storePath, overwrite: true);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Discard(pending);

            throw new CliFailure($"The credential store at {this.storePath} could not be written.", failure);
        }
    }

    /// <summary>Removes a half-written store, leaving the failure that produced it to be reported.</summary>
    /// <remarks>The write has already failed and the previous file is intact, so a residue that cannot be removed is not a second failure worth raising over the first.</remarks>
    private static void Discard(string pending)
    {
        try
        {
            File.Delete(pending);
        }
        catch (Exception residue) when (residue is IOException or UnauthorizedAccessException)
        {
        }
    }
}
