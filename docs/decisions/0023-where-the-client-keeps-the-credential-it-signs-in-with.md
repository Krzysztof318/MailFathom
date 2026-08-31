---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-08-31
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Keep the finished Basic header in the operating system's keychain on the desktop head and for the tab alone on the web head, ask the shell rather than the platform, and say so on the screen where nothing may be kept

<!-- describes: frontend/src/Client.App/**, frontend/src-tauri/** -->

## Context and Problem Statement

The client signs in with HTTP Basic and with nothing else, so what it holds between restarts is a user name and a password rather than a token that expires. There is no renewal to hide the question behind: either the client keeps something that reconstructs the `Authorization` header, or a person types their password every time the application starts. [#1422](https://github.com/Krzysztof318/MailFathom/issues/1422) requires the second not be the answer — *open it and already be signed in* is one of its user stories, and [#1419](https://github.com/Krzysztof318/MailFathom/issues/1419) is titled for keeping somebody signed in across restarts.

Where that value may be kept has a different answer on each head, and neither is obvious. The web head is a page in a browser served from the deployment's own origin: `localStorage` is readable by any script that reaches that origin and outlives the browser, `sessionStorage` dies with the tab, an in-memory value means typing the password on every reload, and a cookie the service sets is a second authentication mechanism rather than a storage choice. The desktop head is a Tauri process beside an operating-system keychain, which is a dependency decision as much as a security one.

This was decided once for the withdrawn client and the record went with it. [#1392](https://github.com/Krzysztof318/MailFathom/issues/1392) removed ADR 0018 along with the client it described, and its answers were about an Uno desktop process, a WebAssembly bundle, and mobile heads that no longer exist. So the decision is retaken against React and Tauri rather than recovered, and the number is not reused.

This record decides where the value lives, what form it takes, what removes it, and what a person is told where nothing may be kept. It lands no screen and no storage: [#1419](https://github.com/Krzysztof318/MailFathom/issues/1419) builds what is named here.

## Decision Drivers

- **What is kept is a password, not a session.** Basic carries no expiry, so a copy that leaks stays valid until somebody changes the password on the server, and nothing in the client can revoke it. Every storage option is therefore judged on what reaches the copy rather than on how long a window it opens.
- **`Basic` is an encoding of the password, not a transformation of it.** `base64(user:password)` reverses in one line, so a decision that says *the header value rather than the password* has decided nothing about exposure and must not read as though it had.
- **The two heads have genuinely different threat models**, and that is the whole reason one answer will not do. A browser origin is shared with every script that reaches it and outlives the application; a desktop process has a keychain the operating system gates and no other origin in its WebView.
- **Nothing in the client tree may branch on the platform.** `frontend/src/AGENTS.md` § *The two heads* refuses `if (isDesktop)`, a per-head component, and a module chosen by target, and permits a difference only where it is a CSS one or belongs to the shell. A decision that gives the heads different storage has to land inside that rule rather than beside it.
- **A crate joins a closure this project distributes.** [ADR 0021](0021-client-stack-react-typescript-tailwind-tauri-and-pnpm.md) already accepted that the desktop shell's crates are linked into a binary MailFathom ships, and [ADR 0016](0016-third-party-licence-obligations-per-artifact.md) reviews `(component, version, artifact)`. Anything added for the desktop head is paid for in both closures' census as well as in a row.
- **Storage limitation is an obligation rather than a preference.** Root `AGENTS.md` requires data minimization and storage limitation to be visible in the architecture, so the client keeps the smallest thing that meets the requirement and keeps it for the shortest time that does.
- **A person on a shared machine has to be able to know the answer** without reading this file. What the client keeps is a sentence on the sign-in screen, not an implementation detail.

## Considered Options

Three axes, decided together because the answers constrain each other.

1. **What is kept at all:** nothing, and the password is typed at every start; the finished `Basic` header value; the user name and the password as separate values.
2. **The web head:** `sessionStorage`; `localStorage`; an in-memory value alone; a cookie the service sets.
3. **The desktop head:** the operating system's keychain through the `keyring` crate, called from a command the shell registers; a community Tauri keychain plugin; `tauri-plugin-stronghold`; the WebView's own `localStorage`; `tauri-plugin-store`, which is a JSON file.

## Decision Outcome

Chosen option: **the finished `Basic` header value, kept in the operating system's keychain where a shell offers one and in `sessionStorage` where none does, bound to the address it was given for, cleared by sign-out and by a refused credential** — because it is the only combination that keeps somebody signed in across restarts on the head that is an application, refuses to leave a permanent password where a later script on the origin can read it, and puts the difference between the heads in the shell rather than in a screen.

### What is stored is the finished header value, and it is one value

`ClientSession` carries `authorization` as a finished header value and `Client.Backend` composes none. What is kept is that same string and nothing beside it.

- **It is not a hash and not a token.** Storing it is storing the password, and every rule below is written on that basis. A reader who takes `Basic Zm9vOmJhcg==` for an opaque credential has misread it, so nothing in the client, in a log line, or on a screen may describe it as one.
- **The user name is not kept a second time.** It is inside the header value already, and a copy beside it would be half the credential stored twice for the sake of prefilling a field. Where the credential is kept there is nothing to prefill; where it is not, nothing is prefilled either.
- **Nothing derived from it is kept**: no fingerprint, no "remember me" marker, no last-signed-in owner. Each would be a second thing to clear and a second thing to leak, for an answer the presence or absence of the credential already gives.
- **It is bound to the address it was given for**, which is the deployment's base address as [#1417](https://github.com/Krzysztof318/MailFathom/issues/1417) resolves it. A stored credential is read back only for that address; where the address has changed, it is discarded rather than sent to a deployment it was never issued against.

### The web head keeps it for the tab and not beyond

`sessionStorage`, holding that one value, written by the single module that signs in and read by nothing else in `Client.App`.

`localStorage` is refused, and the reason is the driver above rather than a general preference. Both are readable by any script that reaches the origin, but only `localStorage` outlives the tab and the browser — which means a script injected next month reads a password stored today, on a machine nobody was using at the time. `sessionStorage` costs almost nothing against an in-memory value under the same script, because anything that can read the object graph can read either, and it buys the reload: a single-page application reloaded by a keystroke or a crash resumes instead of asking for a password again.

A cookie the service sets is not an option on this axis at all. It would be the service issuing a session, which is a second authentication mechanism, out of scope for the parent and a change to `ClientEndpoint` rather than to the client.

**The browser's own password manager is outside this decision.** The sign-in fields are an ordinary form, so a browser may offer to remember what was typed; the client neither relies on that nor tries to suppress it, because suppression is both hostile and widely ignored. What a browser keeps is the person's arrangement with their browser, and nothing in the client reads it.

### The desktop head keeps it in the operating system's keychain

Through the [`keyring`](https://crates.io/crates/keyring) crate, called from a command the shell registers, under one entry naming MailFathom and the deployment address. The shell today registers no command and grants the WebView no capability; this is the first, and it arrives with the `capabilities/` entry that permits exactly it.

- **Windows Credential Manager and the Linux Secret Service** are what that reaches on the two supported desktop targets, and they are what the crate's own default features reach: at 4.2.0 that set is `windows-native-keyring-store` and `zbus-secret-service-keyring-store`, so the pin takes the defaults rather than a feature list of its own. Both stores are gated by the operating system, encrypted at rest, and outside the WebView's own store — which is the whole of what this buys over keeping a password in a file under the user's profile. The crate's kernel-keyring store is deliberately not among them: `linux-keyutils` is memory-resident and does not survive a reboot, so selecting it would keep somebody signed in until the moment the requirement actually applies.
- **A Linux machine with no Secret Service provider running keeps nothing**, and that is a supported outcome rather than a defect. A minimal desktop environment or a machine with no session keyring has nowhere to put a password that meets this decision's bar, so the client keeps it for the run and says so, exactly as the web head does.
- **The entry survives uninstalling the application.** Removing MailFathom does not empty a keychain, so sign-out is the only thing that removes the credential, and the sign-in screen is where a person learns that.

`tauri-plugin-stronghold` is refused on its shape rather than on its quality: it is an encrypted vault unlocked by a password — Tauri's own page requires initialising it with a password hash function — so reaching for it to avoid typing a password means typing a password. It is also not a keychain at all, which is the property being bought here. A community keychain plugin is refused because it is a third-party crate and a third-party npm package wrapping a crate this project can call in about ten lines, which is a supply-chain surface bought for a binding. The WebView's own `localStorage` and `tauri-plugin-store` are the same answer written two ways — a permanent password in cleartext under the user's profile — and they are what the keychain exists to avoid.

### The application asks the shell, never the platform

This is the part that has to be right for `frontend/src/AGENTS.md` to hold.

`Client.App` depends on one credential store with three operations — keep, read back, forget — and on a fourth thing it reports: **whether what it keeps outlives the application**. Which implementation is constructed is decided once, at the composition root, by whether a shell offered the command, and nowhere else. No screen, no component, and no hook learns which head it is running on.

- **The keychain half is Rust, in `frontend/src-tauri/`**, which is where the rule already permits a difference between the heads to live.
- **A screen renders what the store reported**, in the same way it renders a `ClientFailureReason`: one of two sentences, chosen by a value, not by a platform. That is not a head branch, and a store that answers *this does not outlive the application* is answered identically whether it is a browser tab or a Linux machine with no keyring.
- **`Client.Backend` stores nothing and is unchanged by any of this.** It receives a finished header value and sends it; the standing rule that nothing outside it composes or inspects the credential is a constraint this decision holds to, not one it trades for persistence. Where the credential is kept is `Client.App`'s and the shell's; what is done with it on the wire stays that package's alone.

### What clears it

- **Sign-out clears the stored credential**, everything held in memory, and everything derived from the session. On the desktop head that is the keychain entry deleted, not overwritten with an empty value.
- **A refused credential clears it and puts the person in front of the sign-in.** The trigger is the `unauthenticated` failure reason, which is what `Client.Backend` answers for a 401 — the credential the service has stopped accepting. It is *not* `unauthorized`, which is a 403 and means the credential is good and the grant is missing; clearing on that one would sign somebody out for asking about something they may not see. [#1419](https://github.com/Krzysztof318/MailFathom/issues/1419)'s acceptance names `unauthorized` for this, and that is the reason to read this paragraph rather than that line.
- **A changed deployment address discards it**, by the binding above, rather than carrying it to a new address.
- **Closing the tab clears it on the web head**, which is what choosing `sessionStorage` means and what the screen says.
- **Nothing else clears it**, and in particular a failed read that is `unavailable` does not: an unreachable deployment is retried, and signing somebody out because their network dropped is the failure this sentence exists to prevent.

### What the person is told

The sign-in screen says which of the two it is, before they type, in a sentence rather than an icon:

- Where the credential outlives the application, that it will be kept until they sign out, and that signing out is what removes it.
- Where it does not, that it is kept only until they close the client and they will be asked again — **and why**, which is the part that turns a nuisance into a decision somebody can act on: on the web head, because a password left in a browser can be read by anything that reaches the page; on a desktop machine offering no keychain, because the operating system offers nowhere to keep it safely.

Both are catalogue entries in both languages, like every other string.

### Consequences

- Good, because the head that is an application keeps somebody signed in, and the head that is a web page never leaves a permanent password where a later script can find it.
- Good, because one value is stored, in one place, bound to one address, and one act removes it — so what the client holds about a person is answerable in a sentence, which is what storage limitation looks like from the outside.
- Good, because a machine with no keychain is a stated outcome with wording of its own rather than a silent fallback to a worse store.
- Neutral, because the desktop head gains the shell's first command and its first capability, which is reach the WebView did not have and is now permitted for exactly one operation.
- Neutral, because the browser's own password manager may hold the same password regardless, and this decision governs the client rather than the person's browser.
- Bad, because the crate closure grows a family this project distributes, and `keyring` reaches a different operating-system component on each target, so a storage defect can be a property of the machine rather than of the release — the same trade [ADR 0021](0021-client-stack-react-typescript-tailwind-tauri-and-pnpm.md) accepted for the rendering engine.
- Bad, because the web head asks for a password again after every closed tab, which is the cost of refusing `localStorage` and is paid by the person rather than by the code.
- Bad, because a stored password is still a stored password: a keychain raises what it takes to read it and does not make it safe to leak, so nothing here reduces what a compromised machine costs.

## Validation

- `docs/decisions/` is a protected path in `.github/workflows/protected-paths.yml`, so this record's own creation is gated on the owner authoring the change that carries it.
- The `describes:` marker names `frontend/src/Client.App/**` and `frontend/src-tauri/**`, which is what tells a later pull request under either that it is read against this decision. `scripts/review-obligations.sh` and `Fathom review` both resolve it.
- The rule that nothing outside `Client.Backend` sees the credential is already in `frontend/src/AGENTS.md`, and [#1419](https://github.com/Krzysztof318/MailFathom/issues/1419)'s acceptance requires the suite to prove that no password, header value, or anything derived from either reaches a log, an error message, an exception, or telemetry. That is where this decision is enforced rather than asserted.
- The `keyring` pin joins `frontend/src-tauri/Cargo.toml` as an exact version with `Cargo.lock` regenerated beside it, per `frontend/AGENTS.md`, and `$check-docs-licenses` holds it to a `THIRD_PARTY_LICENSES.md` row and to the crate census that file records for the desktop closure. `scripts/update-dependencies.sh --only crates` reads it thereafter. None of that happens in this change: no package is pinned here, and the obligation lands with the code that adds one.

## Pros and Cons of the Options

### Keeping nothing, and typing the password at every start

- Good, because it is the only answer with no stored credential to leak, and it needs no package on either head.
- Good, because it is one behaviour on both heads, so nothing has to reconcile them.
- Bad, because it is refused by the requirement: [#1422](https://github.com/Krzysztof318/MailFathom/issues/1422) wants the client opened already signed in and [#1419](https://github.com/Krzysztof318/MailFathom/issues/1419) is titled for it.
- Bad, because a password typed several times a day is typed somewhere it can be watched, and is chosen for how quickly it types.

### Keeping the user name and the password separately rather than the finished header

- Good, because the sign-in screen could prefill the user name where the password is not kept.
- Neutral, because the storage mechanism is identical either way.
- Bad, because it puts a composing step where `frontend/src/AGENTS.md` says none may be, and the finished value is what `ClientSession` already carries.
- Bad, because it stores two things where one would do, and the second is personal data kept for a convenience.

### `localStorage` on the web head

- Good, because it is the only web option that keeps somebody signed in across a closed browser, which is the desktop behaviour on the head most people would meet first.
- Neutral, because it is exactly as reachable as `sessionStorage` by a script running while the tab is open.
- Bad, because it outlives the tab and the browser, so a script injected long after sign-in reads a password stored long before it — and a Basic password has no expiry to limit that.
- Bad, because it survives on a shared machine somebody has walked away from, with nothing on screen to say it is there.

### An in-memory value alone on the web head

- Good, because nothing is written anywhere, which is the smallest possible answer.
- Neutral, because a script that can read `sessionStorage` can generally read the object graph too, so the security difference against the chosen option is small.
- Bad, because a reload is a cold start, and a single-page application is reloaded by a keystroke, a crash, or a restored tab far more often than a person expects to sign in.

### A cookie the service sets

- Good, because `HttpOnly` puts the value out of reach of every script on the origin, which is stronger than anything in this decision achieves.
- Bad, because it is a different authentication mechanism rather than a storage choice: it makes `ClientEndpoint` issue sessions, which is service work the parent puts out of scope.
- Bad, because it would give the client a second way in, which [#1422](https://github.com/Krzysztof318/MailFathom/issues/1422) refuses by name.

### `tauri-plugin-stronghold` on the desktop head

- Good, because it is a Tauri-maintained plugin with an encrypted store and needs no operating-system component to be present.
- Bad, because it is unlocked by a password, so it answers *where does the password live* with *behind another password*.
- Bad, because it is a vault this project would then own the security of, rather than the operating system's own store, and nothing about a mail client's sign-in needs a general secret database.

### A community Tauri keychain plugin

- Good, because it arrives with a JavaScript binding, so the shell would register no command by hand.
- Neutral, because it wraps the same `keyring` crate the chosen option calls.
- Bad, because it is two third-party artifacts — a crate and an npm package — in place of one, in both closures, for a binding worth about ten lines of Rust.
- Bad, because the maintenance of a small single-purpose plugin is a risk this project would take on for a convenience rather than a capability.

### The WebView's own `localStorage`, or `tauri-plugin-store`, on the desktop head

- Good, because neither needs an operating-system component, so neither has the Linux case where nothing can be kept.
- Good, because `localStorage` needs no code at all: it is the same line the web head runs.
- Bad, because both leave a permanent password in cleartext under the user's profile, where a backup, a synchronised folder, or any process running as that user reads it — which is the exposure a keychain exists to close.
- Bad, because it would make the two heads' storage identical in mechanism while their threat models are not, which is agreement bought by ignoring the difference rather than by resolving it.

## More Information

- [#1419](https://github.com/Krzysztof318/MailFathom/issues/1419) builds what this record names, and [#1417](https://github.com/Krzysztof318/MailFathom/issues/1417) owns the deployment address the stored credential is bound to. [#1422](https://github.com/Krzysztof318/MailFathom/issues/1422) is the parent both sit under.
- ADR 0018 answered this question for the withdrawn Uno client and was removed with it by [#1392](https://github.com/Krzysztof318/MailFathom/issues/1392). Nothing in it is recoverable — it decided for a WebAssembly bundle, an Uno desktop process, and mobile heads none of which exists — and the number is not reused.
- [ADR 0021](0021-client-stack-react-typescript-tailwind-tauri-and-pnpm.md) is what makes this two heads rather than four, and it named *what a session stores client-side* as one of the privacy questions the stack made reachable rather than answered. This is that answer.
- [ADR 0016](0016-third-party-licence-obligations-per-artifact.md) governs how the `keyring` closure is reviewed, per artifact: it reaches the desktop application and nothing else, so the web bundle records it nowhere.
- [ADR 0012](0012-authorization-model-named-permissions-and-where-they-are-enforced.md) is why a refused credential and a missing grant are two different outcomes here rather than one: a permission is enforced in the use case as well as at the transport, so a 403 is an answer about what this owner may do rather than about who they are.
- Revisit this decision if the client gains a way in that issues something expiring, in which case what is kept stops being a password and most of the reasoning above stops applying; if a supported desktop target appears whose keychain the `keyring` crate does not reach; or if the web head acquires an origin it does not share with the deployment.
