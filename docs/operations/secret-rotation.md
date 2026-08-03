# Secret rotation

<!-- describes: src/Infrastructure/Secrets/**, src/Infrastructure/Certificates/** -->

A rotated mailbox password, trust anchor, database credential, or MCP API key takes effect without restarting MailFathom. Rotation is an ordinary operational act, not a maintenance window, and shortening the window in which any single credential is valid is a security and privacy improvement rather than a cost.

Two independent things can change, and both are covered.

| What changed | What MailFathom does |
| --- | --- |
| **The material behind an unchanged reference** — you rewrite the credential file, re-encrypt the systemd credential, or update the vault entry. | Nothing is cached, so the next operation resolves the reference again and observes the new material. No configuration reload is involved because no configuration changed. |
| **The reference itself** — you edit configuration to point at a different credential name or path. | The configuration reload produces a candidate snapshot. It is published only after every reference in it resolves and every trust anchor in it loads; otherwise it is rejected and the previous configuration stays active. |

## What "the next operation" means

Material is applied at operation boundaries, never mid-operation:

| Secret | The operation that picks up a rotation |
| --- | --- |
| Mailbox password | The next connection attempt. A synchronization run that has already authenticated finishes with the credential it authenticated with. |
| Mail account trust anchor | The next connection attempt, which loads the anchor alongside the password. |
| MCP client certificate trust anchor | The next MCP request. Every anchor of the profile being judged is loaded again on each one. |
| Database credential | The next **physical** connection the pool opens. Connections already open finish with the credential they authenticated with, and a pooled logical connection reusing an open physical one keeps it too. |
| MCP API key | The next MCP request. Every configured key is read again on each one, so a rotated file takes effect immediately. |

A long-lived authenticated session has no next connect to pick up a rotation, so its operation boundary is the *connection*: a session whose secrets have rotated is recycled at the next safe point rather than left running for the process lifetime. The IMAP `IDLE` session an account in push mode holds is that case. It is closed and reopened between synchronization runs — never mid-wait — as soon as a newly published configuration snapshot supersedes the one it connected under, and the reconnection resolves every secret again. A reload is not distinguishable from a rotation inside it, so every republished snapshot recycles the session; [push synchronization](../features/imap-synchronization.md#a-long-lived-connection-is-a-rotation-boundary) records why that is the safe direction to err in.

## Lifetimes and what they do

Every secret block states a `Lifetime`: `NoLimit`, or the absolute instant it stops being usable. The instant is absolute rather than a duration precisely so a restart or a configuration reload cannot revive an expired credential.

**Only the MCP API keys enforce it.** An expired key authenticates nothing; a mailbox password, a database credential, and a trust anchor keep working past their stated lifetime and are reported instead:

```
warn: Configuration setting MailSynchronization:Accounts:0:Secrets:Password carries the secret
      imap-primary-password, whose configured lifetime ended at 2026-07-30T00:00:00Z.
```

That line is the reminder that a rotation is due, not the thing that forces it. Setting a lifetime on a secret nothing enforces is still worth doing — the warning is what surfaces a forgotten credential — but it will not take a deployment offline, and it must not be relied on as though it would.

An expired secret never fails startup, in any section. Leaving an expired entry beside its replacement is what a completed rotation looks like.

## Rotating a mailbox password

1. Provision the new credential under the **same** reference the account already names.
2. Verify from the next synchronization interval's log that the account synchronized.
3. Revoke the old credential at the provider.

Rotating the reference instead — pointing `Secrets:Password` at a different credential name — works the same way, but it goes through a configuration reload, so watch for the rejection line described below before revoking anything.

### Native systemd service

```bash
systemd-creds encrypt --name=imap-primary-password new-password.txt /etc/mailfathom/imap-primary-password.cred
```

`LoadCredential=` and `LoadCredentialEncrypted=` populate the credentials directory when the unit starts, so a *rotated file* is not visible to the running process. Reload the unit to republish the directory:

```bash
sudo systemctl reload-or-restart mailfathom
```

This is the one place where the deployment shape, not MailFathom, decides. If uninterrupted rotation matters more than systemd's credential encryption, provision that secret as a file the service user can read and reference it with `file:` instead; MailFathom reads it on the next operation with no unit action at all.

### Containers

A Docker or Podman Compose secret and a Kubernetes Secret both surface as a file. Update the file — for Kubernetes, update the Secret and let the kubelet refresh the projected volume — and the next operation reads it. No restart, no rollout.

```bash
kubectl create secret generic mailfathom-imap --from-literal=imap-primary-password='...' \
  --dry-run=client -o yaml | kubectl apply -f -
```

Projected Secret volumes refresh on the kubelet's own sync period, so allow for that delay before revoking the old credential. A Secret mounted with `subPath` does **not** refresh; mount the directory instead.

## Rotating a mail account's trust anchor

Replace the certificate behind the reference the account names, exactly as for a password, and follow the same per-shape rules above. The next connection attempt loads the new anchor.

Provision the replacement **before** the current one expires and keep the server's own certificate chaining to whichever anchor is active at that moment. There is no overlap mechanism here: an account names one anchor, so the cut-over is the moment the file changes. An MCP client certificate profile is the exception and names several, which is the section below.

Because the chain rebuild does not check revocation, replacing the provisioned material is how a compromised private authority is retired. That is the reason this rotation path matters more for a trust anchor than the equivalent path would for a publicly trusted one.

## Renewing an MCP server certificate

The certificates behind `McpEndpoint:Https:Endpoints` are loaded once, before the server starts, and held for the process
lifetime. Renewing one is therefore a restart rather than a reload, and there is no overlap mechanism: a profile presents
one identity at a time.

1. Provision the renewed certificate behind the reference the profile already names, following the same per-shape rules
   as a mailbox password above.
2. Restart the host.

Startup validates the renewed material before anything listens — that it parses, carries a matching private key, is
inside its validity period, covers the profile's domain, and permits server authentication — so a bad renewal is a host
that refuses to start rather than an endpoint that has quietly stopped working. If any configured profile fails, none is
served, and the failure names the profile and the reason:

```
McpEndpoint:Https:Endpoints:0 — the HTTPS profile 'public' has no usable server certificate [CertificateExpired].
```

Renew before the certificate expires rather than after. Startup reports the expiry of each profile it loaded, and turns
that line into a warning within thirty days:

```
warn: The MCP HTTPS profile public presents a server certificate that expires at 2027-01-31 00:00:00Z. Renew it before
      then: once it expires the profile stops starting, because a certificate outside its validity period is refused
      rather than served.
```

Connections already accepted finish on the certificate they negotiated; the restart is what ends them, as it ends every
other connection.

## Rotating an MCP client certificate authority

A [client certificate profile](mcp-endpoint.md#client-certificates) names several trust anchors precisely so an authority can be replaced without a window in which clients are refused.

1. Provision the successor certificate and add it to that profile's `TrustAnchors` under a **new** `Name`.
2. Restart the host. The endpoint section is read once during composition, so a new entry needs one; the material behind an existing entry does not.
3. Let clients move onto certificates the successor signed. Both authorities are accepted in between, and each request loads the anchors again.
4. Remove the predecessor entry and restart.

Replacing the certificate *behind* an existing reference is the other shape and needs no restart at all: the next request loads what the file now holds. Use it when the authority keeps its identity and only its material changed, and use the overlap above when the authority itself is being replaced.

An anchor that stops loading is recorded at `Error` and skipped, so the rest of that profile keeps working; a profile whose anchors all fail to load refuses every certificate rather than accepting one. Startup loads every configured anchor and fails the host on one that does not, so this state means the deployment changed underneath a running process.

## Rotating an MCP API key

This is the one secret with a real overlap mechanism, because several keys are configured at once and any of them authenticates.

1. Provision the replacement and add it to `McpEndpoint:ApiKeys` under a **new** `Name`.
2. Restart the host. The endpoint section is read once during composition, so a new entry needs one; the material behind an existing entry does not.
3. Move each client onto the new key. Both authenticate in between, so nothing is refused while the change is in flight.
4. Remove the old entry, or give it a `Lifetime` in the past, and restart.

Step 4 has two spellings on purpose. Removing the entry is the clean end state; dating it in the past leaves a record of what was retired and when, and an expired entry authenticates nothing. Either way the retired key stops working and the endpoint stays up throughout.

Rotating the *material* behind an unchanged reference needs no restart at all: every configured key is resolved again on every MCP request, so rewriting the credential file takes effect on the next one. That is the fastest path when a key has to be replaced urgently and the entry itself can stay as it is.

A key whose material disappears is logged by name and refuses requests presenting it; other keys keep working:

```
fail: The material behind MCP API key chatgpt-connector could not be retrieved, so that key cannot authenticate a
      request [MaterialNotFound].
```

## Rotating the database credential

The credential is retrieved when the pool opens a physical connection, so:

1. Create the new credential in PostgreSQL and provision it under the same reference.
2. Existing connections keep working; new physical connections authenticate with the new credential.
3. Revoke the old credential once the pool has turned over — `SELECT * FROM pg_stat_activity WHERE usename = '<old user>'` shows what is still connected.

Both provisioning shapes rotate: `Persistence:Password`, and `Persistence:ConnectionString` where the whole connection string is one secret and its password is re-read from the rotated material. Repointing either reference at a different credential name works too, and goes through the reload path: the candidate is rejected if the new reference does not resolve, leaving the previous one active.

**Two shapes still need a restart, and both are refused rather than half-applied.**

*Changing where the credential comes from.* The pool attaches its password provider once, when it is built, so adding `Persistence:Password` to a deployment that started without one — or removing it, or switching to `Persistence:ConnectionString` — is not a rotation. A reload that does so is rejected with `CredentialSourceChangeRequiresRestart` and the previous settings stay active, rather than being logged as adopted while every connection keeps using what startup composed. Restart to change the shape; rotate freely within it.

*A password written into `ConnectionStrings:mailfathom` with no secret block* — an orchestrator-injected connection string. Nothing re-reads it, and under `ReferenceOnly` startup already logs a warning naming it. The same restriction applies to the non-credential parts of `Persistence:ConnectionString`: a rotated connection string that also changes host, database, or user name describes a different database rather than a rotated credential, and only its password is adopted in place.

A rotated `Persistence:ConnectionString` is also parsed before it is published. Material that resolves but is not a valid connection string, or that no longer carries a password when it is what supplies the credential, is rejected as `ConnectionStringNotParsable` or `ConnectionStringCarriesNoPassword` — otherwise it would replace working settings and then fail every connection opened afterwards.

## Watching a reload

A rejected candidate is logged at `Error` with the configuration path and a stable failure identity, and the previous configuration stays active:

```
Rejected a reloaded mail synchronization configuration and kept the previous one active. MailSynchronization:Accounts:0:Secrets:Password — the secret reference could not be resolved [MaterialNotFound].
```

An adopted one is logged at `Information`:

```
Adopted a reloaded mail synchronization configuration; new operations use its secret references.
```

No log line, exception, or diagnostic carries the reference target, the environment variable's value, or any part of the material. A loaded trust anchor is the one exception and only in the sense that a certificate is public: it is logged by subject and thumbprint.

Treat a rejection as an unfinished rotation. MailFathom keeps running on what it had, so nothing breaks immediately — but the deployment is now running configuration that differs from what is on disk, and revoking the old credential at that point is what would take it offline.
