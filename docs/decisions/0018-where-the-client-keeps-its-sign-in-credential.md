---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-08-25
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Keep a sign-in credential only where the operating system holds a secret for one user, persist nothing on the browser head, store the owner's password rather than anything derived from it, and let a command that must keep working fall back to a sealed file

<!-- describes: backend/src/Cli/Credentials/**, backend/src/Cli/Commands/LoginCommand.cs, backend/src/Cli/Commands/LogoutCommand.cs, backend/src/Cli/Administration/DeploymentAccess.cs, frontend/src/Client.Backend/Authorization/IOwnerCredentialStore.cs, frontend/src/Client.Backend/Authorization/UnkeptOwnerCredentialStore.cs, frontend/src/Client/Platforms/Desktop/Credentials/** -->

## Context and Problem Statement

The client keeps the credential it signs in with in memory for the process's lifetime and nowhere else — no file, no browser storage, no platform credential store — and `frontend/src/AGENTS.md` states that as a rule together with its reasoning. The session therefore ends when the process does. That is correct for a client nobody uses daily and wrong for one somebody lives in: a mail application that asks for a password every time it starts is one people stop opening.

What the credential *is* decides what the question costs. [#1146](https://github.com/Krzysztof318/MailFathom/issues/1146) makes HTTP Basic against an owner's username and password the only way into the client, [#1120](https://github.com/Krzysztof318/MailFathom/issues/1120) is the credential it presents, and [#1148](https://github.com/Krzysztof318/MailFathom/issues/1148) removes the authorization-code path that is still in `Client.Backend`. So there is no token to expire, nothing to renew, and no short life that could compensate for a store being weak: the value is the owner's password, it goes on the wire on every request, it stays valid until an administrator rotates it, and it is the kind of secret a person may have reused somewhere this project will never hear about.

The decision question is four questions that an implementation would otherwise answer by accident, in the place it is cheapest to answer them:

- whether the credential survives the process at all, and on which heads;
- what holds it on each, given that `net10.0-desktop` is one target framework running on three operating systems and `net10.0-browserwasm` is a document served from an origin;
- what is stored — the password, or something derived from it;
- what happens when the store is absent or refuses, and what sign-out clears.

`mfctl` asks the same question about different material and is answered here rather than in an adapter comment. The command keeps the credential it administers a deployment with in `credentials.json` under the operator's per-user directory, sealed with AES-256-GCM under a random key kept beside the store. The test below — *is this, on its own, enough to act as the owner?* — reaches that token, the OAuth refresh token beside it, and any API key exactly as it reaches the client's password, so one record governs both surfaces rather than two that happen to agree.

What the command must not do is take the client's answer unchanged, because two of the premises above do not hold for it:

- **A process is not a session.** The client's answer where there is no store is to hold the credential in memory for the process's lifetime, which for a window somebody leaves open is a working session. `mfctl` exits after every command, so the same rule would mean signing in on each invocation — that is not a weaker session, it is no session at all.
- **The command runs where a desktop does not.** A headless server, a jump host, or a container may have no D-Bus session bus and no Secret Service provider installed, so the store is not merely locked: it does not exist. The command has to keep working there, and a screen that explains itself is not available to it.

Recorded on issue [#1140](https://github.com/Krzysztof318/MailFathom/issues/1140). [#1148](https://github.com/Krzysztof318/MailFathom/issues/1148) builds the client's half; [#1274](https://github.com/Krzysztof318/MailFathom/issues/1274) built the command's, and takes the storage half of [#318](https://github.com/Krzysztof318/MailFathom/issues/318) with it.

## Decision Drivers

- **The client is a public client on every head.** A desktop binary and a WebAssembly bundle are both readable by whoever runs them, so nothing here is protected by a secret the application holds. Anything that protects a stored credential has to be protection the operating system provides, never protection the client implements over storage the operating system left open.
- **A stored password is worth more to an attacker than a stored token, and cannot be given a short life to compensate.** Revoking it is an administrative act under [#1120](https://github.com/Krzysztof318/MailFathom/issues/1120) rather than a lapse of time, and the deployment mints nothing else the client could hold in its place.
- **The heads do not have comparable stores, and one of them has none at all.** A single answer applied everywhere is therefore either too weak for the heads that can do better or a refusal to serve the heads that can.
- **Every request carries the password anyway.** `DeploymentAddressRule` refusing a clear-text address to anything but this machine is what stands between the password and whatever is on the path, and a decision about storage that ignored the wire would be protecting the smaller half.
- **Root `AGENTS.md` classifies credentials as sensitive by default and requires data minimization and storage limitation to be visible in the architecture.** A credential kept because it was convenient, in the first place that would hold it, is exactly the shape those obligations exist to refuse.
- **The answer has to be per head and stated rather than general**, because the operator deciding whether the browser head may hold their password cannot read that off an intention.
- **A surface that cannot ask again has to keep working where the store is absent.** A window can end its session and meet the person next time; a command that exits after every invocation cannot, so refusing to persist on a host with no keyring would not weaken `mfctl` — it would stop it. That difference is what makes one fallback correct on one surface and refused on the other, and it is a property of the surface rather than of the credential.

## Considered Options

- Keep nothing anywhere: the credential lives in memory for the process's lifetime on every head, which is today's rule.
- Keep it on every head, in the storage that head already has — `ApplicationData.Current.LocalSettings`, which `IDeploymentChoiceStore` already uses for the deployment address.
- Keep it only where the operating system holds a secret for one user, and persist nothing on the browser head.
- Keep something derived from the password instead of the password, so that every head including the browser could hold it.

## Decision Outcome

Chosen option: "Keep it only where the operating system holds a secret for one user, and persist nothing on the browser head", because it is the only option whose answer changes where the platforms actually differ — it gives the heads with an operating system behind them the thing the issue asked for, and it refuses the head where every available store is readable by any script that reaches the page.

### What is stored, and what is refused

Three named values: the deployment address the credential belongs to, the username, and the password. The `Authorization` header is composed per request in the one place that already composes it, rather than stored ready-made — the client needs the username on its own anyway, to say who it will sign in as, and storing one value that has to be split back apart is worse than storing two that are already named.

The password itself is what is stored, because HTTP Basic sends the password and nothing derived from it can take its place. Three alternatives are refused by name:

- **Anything the deployment would accept *instead of* the password.** [#1120](https://github.com/Krzysztof318/MailFathom/issues/1120) issues no such thing, and minting one here would be a second authentication method invented by a client rather than by the surface that admits it — long-lived, since nothing would expire it, and unrevokable, since nothing under [#1120](https://github.com/Krzysztof318/MailFathom/issues/1120) could withdraw it without rotating the password it stood for. A store holding that would be strictly easier to use than a stolen password, because using it would not require knowing the password at all.
- **The password under a key the client also stores.** That is obfuscation, and its entire effect is on the sentence describing it: the store is exactly as easy to open, while a reader told the credential is encrypted at rest concludes something untrue.
- **Sending anything but the password.** A client-side hash presented in the Basic field is a password-equivalent under another name — RFC 7617 defines that field as the password and [#1120](https://github.com/Krzysztof318/MailFathom/issues/1120) hashes whatever arrives — so it protects nothing and breaks the scheme for every other client of the same endpoint.

Nothing else of the session goes into the store or beside it: not the session document, not the owner's grants, not the account list, and no mail.

### Where each head keeps it

**The port is declared in `Client.Backend` and implemented per head.** `Client.Backend` targets plain `net10.0` and references no Uno package and no WinUI assembly, so nothing that needs one can sit in it; a head's implementation lives under `frontend/src/Client/Platforms/`, reaches that platform's own store, and exposes the value to nothing but the port. `BrowserSignInRedirectListener` is already that arrangement for a port of the same shape.

**The desktop head reaches its operating system directly.** `net10.0-desktop` is one target framework running on Windows, Linux, and macOS, and Uno's own credential store does not reach it: `Windows.Security.Credentials.PasswordVault` is marked unsupported on the Skia targets, so a head that renders through Skia on every operating system cannot rest on it. There is no cross-platform store to take, and the port has three implementations:

- **Windows** — the Credential Manager, holding a generic credential under the logged-on user's profile. The Data Protection API at `DataProtectionScope.CurrentUser` is deliberately *not* offered as an alternative: it protects a value to the same user account but stores nothing, so taking it would leave whoever implements it choosing where the ciphertext is written, and the two locations nearest to hand are the two this record refuses below. The Credential Manager is protected under that profile by the same mechanism and answers where as well as how.
- **macOS** — Keychain Services, as a generic password item in the login keychain, scoped to this application and not synchronized to iCloud.
- **Linux** — the Secret Service API over D-Bus, which GNOME Keyring and KWallet implement, storing the item in the session's default collection. Two separate absences put a session outside that: no D-Bus session bus to reach a provider over, and no provider installed to answer on one. Neither is a service that has merely stopped, and a session in either state has no store — which is the case below rather than a licence to write a file.

**The browser head keeps nothing.** `localStorage`, `sessionStorage`, IndexedDB, and cookies are scoped to the origin rather than to a person, so any script running on the origin reads them: one injected script, one compromised dependency inside the bundle, or one extension with access to the page lifts an owner's password rather than a token that would have expired. Encrypting the value only moves the question to where the key lives, and the answer is the same storage. Uno reaches the same conclusion for its own credential API and throws `NotSupportedException` on WebAssembly rather than implementing it over origin storage. The session ends when the document is unloaded, and the person signs in again.

**The mobile heads take Uno's own credential store when they are opened.** `net10.0-android` and `net10.0-ios` are not built yet; `PasswordVault` is implemented on both and is backed by the platform mechanism this decision is about — a key held in the Android Keystore, and the iOS Keychain — so those heads implement the port over it rather than reaching their operating systems by hand. What each needs beyond that, such as a keychain-access entitlement on Apple platforms, arrives with the head rather than here.

### Where the command keeps it, and what it stores

**`mfctl` reaches the same two stores by the same test, behind one port inside `Cli`.** What goes into them is a profile's two secrets and nothing else: the bearer credential the profile presents, and the refresh token an OAuth session renews it with. Both pass the test — either is enough to act as the owner against that deployment — and both are keyed by the deployment address the profile holds *and* by the profile holding it. The address is what keeps one deployment's credential from ever being presented to another, and it is not sufficient on its own: two profiles may name one deployment under different credentials, an administrator's and a read-only one, and a key derived from the address alone would let the second sign-in overwrite the first, after which each profile would silently present the other's identity.

- **Windows** — the Credential Manager, as a generic credential persisted for the logged-on user on that computer. The Data Protection API is refused here for the reason it is refused above: it protects a value without holding one, and the location it would then need is the file this decision is moving away from.
- **Linux** — the Secret Service through `libsecret`, in the session's default collection, exactly as the desktop head reaches it.
- **macOS is not implemented**, because the command is published for Windows and Linux only. Keychain Services would be an implementation nothing this project ships would run, and saying so is more useful than describing a platform that has no binary.

Both are reached by P/Invoke rather than through a package. There is no managed API for either store, the entry points are stable, and a package would be a supply-chain and licensing decision under [ADR 0016](0016-third-party-licence-obligations-per-artifact.md) bought for a handful of declarations — while `libsecret` itself is a runtime dependency of the operating system rather than a component this project distributes.

**What is left in `credentials.json` is what a profile *is* rather than what it can do**: the deployment address, the credential's reported name, a key-pair profile's private-key path, an OAuth session's endpoint, issuer, client, resource, scopes, and expiry, and the transport trust the operator accepted. None of those is a secret; each of them is in clear today and stays there. The absence of a secret member is itself the statement that the store holds it — except on a key-pair profile, whose own member is read first and says that no store holds anything for it.

**A key-pair profile stores nothing in either place**, because it stores nothing today: the private key stays where the operator generated it, under the protection they gave that file, and every command mints its own short-lived assertion from it. Moving that key into a secret store would undo the one property the method exists for.

**A profile written before there was a store moves into one by itself**, on the first command that opens it rather than at a sign-in — that is the one moment both secrets are already in hand, so the upgrade costs the operator nothing. The store is written first and the file second, so an interruption leaves the sealed profile readable and the entries merely duplicated. **The key file is removed once no profile is sealed under it**, because material that protects nothing is material still worth stealing.

**Signing out clears both halves**, and an entry that cannot be cleared is reported rather than left behind: the profile is genuinely forgotten either way, and only the operator can open a keyring that has since locked. **Signing in again after a deployment has moved clears the entries the profile left at its old address**, for the same reason and at the only moment it can: a profile keeps its name when its deployment changes port, so everything afterwards reads the address it now holds and nothing would ever look at the old key again.

### When there is no store, or it refuses

The client keeps the credential in memory for the process, exactly as it does today, and says so on the screen where the person signed in: that this machine will ask again next time, and why. It never falls back to somewhere weaker — no file beside the binary, no `ApplicationData.Current.LocalSettings`, no browser storage. `IDeploymentChoiceStore` writes the deployment address to `LocalSettings` and goes on doing so; an address is not a secret, and the two must not be confused precisely because they would otherwise sit next to each other.

A Linux session with no Secret Service, a keychain the person declines to unlock, and a keystore reporting its key invalidated after the device credentials changed are one case with one answer, and each of them is a sentence to the person rather than a silently weaker session.

**`mfctl` answers the same case differently, and this is the one place the two surfaces part.** Where there is no store — `libsecret` absent, no D-Bus session bus, no provider running, a locked collection, or the store refusing — the command seals the profile's secrets into `credentials.json` under a random key kept beside it, exactly as it did before this decision, and **says which of the two took them**. The client's refusal to fall back is right because a window can meet the person again; a command that exits after every invocation would be refusing to work at all, on precisely the hosts a deployment is administered from. What makes the weaker arrangement acceptable is that it is stated: an operator on a jump host reads which storage they got at the moment they sign in, rather than inferring it from a file.

The fallback is per profile and whole. A store that took one of a profile's two secrets and refused the other would leave a session split between two places and openable from neither, so the first entry is withdrawn and the profile is sealed entire. Reading one back is then a single decision rather than a search.

The withdrawal is the one step of that which can itself fail, and it is reported rather than treated as an unlucky rollback. The ordinary way the second write refuses is a collection locking mid-command, and a locked collection will not give the first entry up either — which leaves a live credential in the operator's keyring under a profile whose file entry says it is sealed, so nothing the command runs later goes looking for it. Signing in says what was left behind, on the same terms as signing out does.

A secret the store *did* take and can no longer produce is not fallen back from. A locked collection and a removed entry are different facts — one is answered by unlocking, the other by signing in again — so the command names which it met instead of reporting a file it never wrote.

A stored credential the deployment then refuses is cleared, and the person meets the sign-in. The store holds a copy of something the deployment owns, so the deployment's answer wins; an item that cannot be read or does not parse is cleared for the same reason.

The item is keyed by the deployment address that was signed into, so one deployment's password is never presented to another. **At most one item is held at any moment**, and it takes three rules rather than one, because the address can change without the running process seeing it move and can be absent altogether:

- **Pointing the client elsewhere clears the item for the address being left.** `DeploymentAddress` already drops the in-memory copy when the address moves, and the stored half follows it rather than merely failing to match.
- **Starting the client reconciles the store against the address it comes up pointed at**, and clears any item held for another. That second half is not redundant: `DeploymentAddress` reports a move only within one process, so an address that changed between runs — a kept choice `DeploymentChoice.Restore` forgot because it no longer passes the rule, or an installation whose stated address was re-pointed — leaves the client running against B while the store still holds A's password, and no move ever fires to clear it.
- **A start that resolves no address at all clears any item held**, because reconciliation runs on every start rather than only on one that resolved somewhere. Coming up pointed at nothing is an ordinary state — `DeploymentChoice.Restore` answering nothing opens the screen that asks — and it is the state in which neither of the two mechanisms above has an address to work from: nothing to reconcile against, and no move to report when a first address is then chosen, since `PointAt` reports one only when it is replacing an address it already held. A client pointed nowhere is pointed at no deployment, so no item it holds belongs to where it is pointed. The address was already forgotten rather than treated as fatal; the credential is forgotten with it.

The reason for all three is the same one: the client points at one deployment at a time, so a credential for a deployment it is not pointed at is a password nothing will ever present, which is precisely what storage limitation refuses to keep. Somebody who moves between two deployments therefore signs in each time they move, which is the cost of not accumulating passwords for deployments nobody is using.

### What sign-out clears, and what it does not

Sign-out clears the stored item for the current deployment, the copy in memory, and everything derived from the session. On the browser head there is nothing stored, so it clears the memory, and the person is not told a store was emptied that never existed.

**Sign-out is local and revokes nothing.** Basic has no server-side session to end, so the password stays valid on that deployment until an administrator rotates it under [#1120](https://github.com/Krzysztof318/MailFathom/issues/1120). That is a property of the credential rather than of the storage, and the interface must not offer sign-out as though it were revocation.

Nothing is added to the sign-in screen. [#1148](https://github.com/Krzysztof318/MailFathom/issues/1148) fixes it at a username and a password and nothing else, and this decision keeps it there: persistence is what happens wherever it is permitted. The case a "remember me" control would serve — two people sharing one operating-system account — is a case none of these stores can answer either, because both people are the same user to the operating system. What answers it is sign-out.

### The threat model this accepts

- **`mfctl` on a host with a store accepts the desktop model below.** On a host without one it accepts the weaker one the sealed file always had: the credentials file discloses nothing on its own when it is copied away, because the key is random and kept beside it rather than derived from anything nameable — and on Windows that key file is additionally wrapped with DPAPI under the current user — while anything already able to read files as this operator reads the key as easily as the store. The file mode answers another user of the same machine; the sealing answers the copy; nothing answers code running as the operator. That is a real reduction against a keyring, which is why it is the fallback and not the design.
- **On desktop and mobile, an attacker must already be able to run code as that operating-system user, or hold the device unlocked.** These stores defend against a *different* user of the same machine, against the disk being read outside the running system, and against the item arriving somewhere else in a backup. They do not defend against code running as the person, which can ask the same store the same question and be answered. No store on any of these platforms does, and this record says so rather than implying a boundary that is not there.
- **On the browser head nothing is stored**, so what an attacker reaches is a running document, and only while it runs. What is paid for that is the head with the lowest friction to reach asking for a password on every visit.
- **On every head the password is a managed string in memory for as long as somebody is signed in**, and .NET offers no reliable way to remove it — `SecureString` is documented as not to be used for new development and is not encrypted on every platform. A memory dump of a signed-in process yields the password on the browser head too, so this is what the process always cost rather than something persistence introduced.
- **On the wire the password is on every request, encoded rather than encrypted.** `DeploymentAddressRule` refusing a clear-text address to anything but this machine is the whole of what stands between it and the path, which is why it is a rule the assembly enforces rather than a preference a screen expresses.

### What a second sign-in method changes

This record is written about credential material rather than about Basic, so a later method's material is placed by the same test: is this, on its own, enough to act as the owner? If it is, it goes only where the operating system holds a secret for one user, and the browser head persists none of it — a second password or a long-lived API key is answered here already and needs no record of its own.

Only material that fails that test reopens the question. Something bound to the device, or short-lived, or revocable without touching the owner's password, could justify browser persistence, and that is a different weighing rather than an application of this one. A parent adding such a method writes an ADR that supersedes this record and says which head's answer changed.

### Consequences

- Good, because opening the client is opening it on the heads people install it on, and the head that cannot protect a password does not pretend to.
- Good, because the answer is stated per platform, so an operator deciding whether the browser head may hold their password reads it rather than infers it.
- Good, because nothing new is invented to store: no token the deployment never issued, and no key kept beside the value it claims to protect.
- Neutral, because the desktop head gains three operating-system implementations behind one port instead of one call to a framework API. Where any of them is reached through a package rather than by hand, that package is a supply-chain and licensing decision under [ADR 0016](0016-third-party-licence-obligations-per-artifact.md) like any other.
- Neutral, because the deployment address goes on living in `ApplicationData.Current.LocalSettings` while the credential does not. Two stores rather than one is the point of the decision rather than a cost of it.
- Bad, because somebody who uses only the browser head gets none of what the issue asked for, and the reason is a property of browsers rather than anything this project can improve.
- Bad, because a Linux desktop with no keyring running is in the same position, and the client can only say so.
- Bad, because a password at rest on a desktop is reachable by anything running as that person, and nothing here narrows that.
- Bad, because sign-out looks like revocation and is not, so the interface carries a distinction a person did not ask for.
- Bad, because somebody who moves between two deployments signs in on every move, since at most one item is held and pointing the client away clears the one it is leaving.
- Neutral, because `mfctl` now carries two storage arrangements rather than one, and every command that opens a profile has to know which it is under. The file says so by what it does not contain, which is one fact in one place rather than a mode recorded twice.
- Bad, because an operator on a host with no keyring gets the weaker arrangement and a sentence about it, which is a distinction they did not ask to learn — and the alternative was a command that refuses to work on the hosts deployments are actually administered from.
- Bad, because a keyring that locks or goes away between two commands turns a working profile into one that cannot be opened until it is unlocked, where the sealed file had no such state. That is the cost of the store being protected by something other than this command.

## Validation

- `Cli` declares its own port and both platform implementations, and `CredentialStore` is the only thing that reads a secret back out of either. `backend/tests/Cli.UnitTests` covers what the decision creates: an unavailable store leaving the sealed file in use, a store that accepts leaving no secret in the file, the move off the key file and the key file going with it, `logout` clearing both halves, the address key keeping one deployment's credential away from another, and a store failure being reported rather than swallowed. Neither platform call is unit-tested and neither could be — one needs a Windows logon session and the other a session bus and a running keyring — so review of the two adapters is what checks them, as it is for the client's heads.
- `Client.Backend` declares the port and is the only thing that reads the credential back out of it. A head's implementation under `frontend/src/Client/Platforms/` reaches that platform's store and hands the value to nothing else, which is the arrangement `BrowserSignInRedirectListener` already has for a port of the same shape. Both halves are checked the same way — by review of what each side makes public, neither of which offers a credential to a screen.
- The browser head implements the port by reporting that it has no store, rather than by registering nothing. A head with no implementation is a missing registration somewhere; a head with an implementation that answers "none" is a behaviour a test can assert.
- `frontend/tests/Client.UnitTests` covers the paths this record creates: a store that is unavailable leaving the credential in memory, a deployment refusal clearing the stored item, sign-out clearing it, the address key keeping one deployment's credential away from another, pointing the client elsewhere leaving no item behind for the address it left, a start whose address differs from the stored item's clearing that item rather than carrying it, and a start that resolves no address clearing it too.
- Review is what checks the per-head mechanism, because no analyzer distinguishes a Secret Service call from a file write. `frontend/src/AGENTS.md` states the outcome, so a change that puts the credential somewhere else contradicts an instruction file every session working in that stack loads.
- `$check-docs-licenses` covers any component taken to reach an operating system's store, under [ADR 0016](0016-third-party-licence-obligations-per-artifact.md).

## Pros and Cons of the Options

### Keep nothing anywhere

The rule as it stands: the credential lives in memory for the process's lifetime on every head.

- Good, because there is no store to attack, no store to clear, and nothing to be wrong about per platform.
- Good, because it needs no interop, no package, and no per-head implementation.
- Neutral, because it is the correct answer for the browser head, and this decision keeps it there.
- Bad, because it is the problem [#1140](https://github.com/Krzysztof318/MailFathom/issues/1140) was opened about: a client that asks for a password every time it starts is one people stop opening, and a mail client is opened every day.
- Bad, because a person who types a password several times a day chooses a shorter one, so the rule that stores nothing quietly weakens the thing it was protecting.

### Keep it on every head, in the storage that head already has

`ApplicationData.Current.LocalSettings`, behind which sits a per-user preferences file on a desktop and the browser's origin storage in the browser head — the store `IDeploymentChoiceStore` already uses for the deployment address.

- Good, because it is one mechanism, already present, already used, and needs nothing per platform.
- Good, because every head then opens already signed in.
- Bad, because it is a preferences store rather than a secret store: on a desktop it is a readable file under the user's profile, and in the browser it is origin storage any script reads. The owner's password would sit in it in the clear.
- Bad, because it puts the credential next to the deployment address in the same store, which is how a later change moves a secret without anybody noticing it was one.
- Bad, because the browser head's answer would be decided by what was convenient on the desktop, which is the failure this record exists to prevent.

### Keep it only where the operating system holds a secret for one user

The chosen option, described above.

- Good, because each head's answer follows what that head can actually enforce, and the head with no answer is told to keep nothing.
- Good, because the accepted threat model is stated rather than implied, including the part these stores do not defend against.
- Good, because what is stored is the password and only the password, so a stolen store is no easier to use than a stolen password.
- Neutral, because the browser head keeps behaving as it does today, which is both the weakest outcome for its users and the only correct one.
- Bad, because the desktop head carries three implementations, and each of the three fails in its own way — an absent D-Bus service, a locked keychain, a profile that cannot be decrypted after a machine transfer.
- Bad, because the decision is only as good as its implementations, and nothing automated distinguishes a store that protects from one that merely persists.

### Keep something derived from the password

Exchange the password once for a long-lived value the client holds instead, so that every head, the browser included, could persist something that is not the password.

- Good, because a value distinct from the password could in principle be revoked without rotating it, and could be scoped more narrowly than the owner's full grant.
- Neutral, because it is the shape a future sign-in method might genuinely take, which is why this record names the conditions under which it would be reconsidered rather than refusing it forever.
- Bad, because nothing issues it. [#1120](https://github.com/Krzysztof318/MailFathom/issues/1120) mints only a password, so the client would be inventing an authentication method for a surface that does not offer one.
- Bad, because without a revocation path the derived value is a password that never changes and that nobody can withdraw — a store holding it is easier to use than a stolen password rather than harder, because opening it does not require knowing the password.
- Bad, because in the browser head it would still sit in origin storage, so the head that motivated the option gains a stealable long-lived credential in exchange for the one it currently does not keep.

## More Information

- [ADR 0005](0005-data-encryption-key-ring-and-provisioning.md) governs sensitive material the service holds at rest; this record is its counterpart on the client, where there is no deployment-managed key ring and the operating system is the only thing that can play that part.
- [ADR 0012](0012-authorization-model-named-permissions-and-where-they-are-enforced.md) governs what the presented credential is then allowed to do, which is unchanged by where it was kept.
- [ADR 0016](0016-third-party-licence-obligations-per-artifact.md) governs any component taken to reach a platform store.
- [#1148](https://github.com/Krzysztof318/MailFathom/issues/1148) implements this and carries the acceptance items that follow from it; [#1146](https://github.com/Krzysztof318/MailFathom/issues/1146) is the parent that cannot close until it lands.
- The `describes:` marker names `backend/src/Cli/Credentials/**` because [#1274](https://github.com/Krzysztof318/MailFathom/issues/1274) landed the command's port and its two platform implementations, and names `LoginCommand.cs`, `LogoutCommand.cs`, and `Administration/DeploymentAccess.cs` beside it because three of the decisions above are implemented there rather than under `Credentials/`: which of the two places took the secrets is said by the sign-in, both halves are cleared by the sign-out, and what an interrupted move left behind is reported by the seam every other command settles its deployment on. It gains the client's paths when [#1148](https://github.com/Krzysztof318/MailFathom/issues/1148) lands the per-head implementations, which is one of the two edits an accepted ADR is permitted.
- [#318](https://github.com/Krzysztof318/MailFathom/issues/318) asked for the command's storage and its installation together, on the reasoning that only an installed command may depend on a secret service. Naming the fallback here is what unpairs them: with the weaker storage stated rather than accidental, the store is taken wherever it is present without waiting for packaging, and #318 keeps the installation half.
- [`operations/admin-endpoint.md`](../operations/admin-endpoint.md) states where the command keeps a credential on each platform and what the fallback means, for the operator rather than for a reader of this record.
- Three things would reopen this: a sign-in method whose long-lived material is not password-equivalent, which supersedes the record for the head it changes; Uno implementing a credential store for the Skia desktop targets, which would replace three implementations with one while changing nothing decided here; and a browser capability that scopes a secret to something narrower than the origin, which is the only development that would make the browser head's answer worth reconsidering.
