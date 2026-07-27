# Secret rotation

A rotated mailbox password, trust anchor, or database credential takes effect without restarting MailMcp. Rotation is an ordinary operational act, not a maintenance window, and shortening the window in which any single credential is valid is a security and privacy improvement rather than a cost.

Two independent things can change, and both are covered.

| What changed | What MailMcp does |
| --- | --- |
| **The material behind an unchanged reference** — you rewrite the credential file, re-encrypt the systemd credential, or update the vault entry. | Nothing is cached, so the next operation resolves the reference again and observes the new material. No configuration reload is involved because no configuration changed. |
| **The reference itself** — you edit configuration to point at a different credential name or path. | The configuration reload produces a candidate snapshot. It is published only after every reference in it resolves and every trust anchor in it loads; otherwise it is rejected and the previous configuration stays active. |

## What "the next operation" means

Material is applied at operation boundaries, never mid-operation:

| Secret | The operation that picks up a rotation |
| --- | --- |
| Mailbox password | The next connection attempt. A synchronization run that has already authenticated finishes with the credential it authenticated with. |
| Trust anchor | The next connection attempt, which loads the anchor alongside the password. |
| Database credential | The next **physical** connection the pool opens. Connections already open finish with the credential they authenticated with, and a pooled logical connection reusing an open physical one keeps it too. |

A long-lived authenticated session has no next connect to pick up a rotation, so its operation boundary is the *connection*: a session whose secrets have rotated is recycled at the next safe point rather than left running for the process lifetime. No such session exists yet — IMAP IDLE is later work — but that is the rule it will be built to.

## Rotating a mailbox password

1. Provision the new credential under the **same** reference the account already names.
2. Verify from the next synchronization interval's log that the account synchronized.
3. Revoke the old credential at the provider.

Rotating the reference instead — pointing `Secrets:Password` at a different credential name — works the same way, but it goes through a configuration reload, so watch for the rejection line described below before revoking anything.

### Native systemd service

```bash
systemd-creds encrypt --name=imap-primary-password new-password.txt /etc/mailmcp/imap-primary-password.cred
```

`LoadCredential=` and `LoadCredentialEncrypted=` populate the credentials directory when the unit starts, so a *rotated file* is not visible to the running process. Reload the unit to republish the directory:

```bash
sudo systemctl reload-or-restart mailmcp
```

This is the one place where the deployment shape, not MailMcp, decides. If uninterrupted rotation matters more than systemd's credential encryption, provision that secret as a file the service user can read and reference it with `file:` instead; MailMcp reads it on the next operation with no unit action at all.

### Containers

A Docker or Podman Compose secret and a Kubernetes Secret both surface as a file. Update the file — for Kubernetes, update the Secret and let the kubelet refresh the projected volume — and the next operation reads it. No restart, no rollout.

```bash
kubectl create secret generic mailmcp-imap --from-literal=imap-primary-password='...' \
  --dry-run=client -o yaml | kubectl apply -f -
```

Projected Secret volumes refresh on the kubelet's own sync period, so allow for that delay before revoking the old credential. A Secret mounted with `subPath` does **not** refresh; mount the directory instead.

## Rotating a trust anchor

Replace the certificate behind the reference the account names, exactly as for a password, and follow the same per-shape rules above. The next connection attempt loads the new anchor.

Provision the replacement **before** the current one expires and keep the server's own certificate chaining to whichever anchor is active at that moment. There is no overlap mechanism: one anchor is configured at a time, so the cut-over is the moment the file changes.

Because the chain rebuild does not check revocation, replacing the provisioned material is how a compromised private authority is retired. That is the reason this rotation path matters more for a trust anchor than the equivalent path would for a publicly trusted one.

## Rotating the database credential

The credential is retrieved when the pool opens a physical connection, so:

1. Create the new credential in PostgreSQL and provision it under the same reference.
2. Existing connections keep working; new physical connections authenticate with the new credential.
3. Revoke the old credential once the pool has turned over — `SELECT * FROM pg_stat_activity WHERE usename = '<old user>'` shows what is still connected.

Both provisioning shapes rotate: `Persistence:Password`, and `Persistence:ConnectionString` where the whole connection string is one secret and its password is re-read from the rotated material. Repointing either reference at a different credential name works too, and goes through the reload path: the candidate is rejected if the new reference does not resolve, leaving the previous one active.

**Two shapes still need a restart, and both are refused rather than half-applied.**

*Changing where the credential comes from.* The pool attaches its password provider once, when it is built, so adding `Persistence:Password` to a deployment that started without one — or removing it, or switching to `Persistence:ConnectionString` — is not a rotation. A reload that does so is rejected with `CredentialSourceChangeRequiresRestart` and the previous settings stay active, rather than being logged as adopted while every connection keeps using what startup composed. Restart to change the shape; rotate freely within it.

*A password written into `ConnectionStrings:mailmcp` with no secret block* — an orchestrator-injected connection string. Nothing re-reads it, and under `ReferenceOnly` startup already logs a warning naming it. The same restriction applies to the non-credential parts of `Persistence:ConnectionString`: a rotated connection string that also changes host, database, or user name describes a different database rather than a rotated credential, and only its password is adopted in place.

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

Treat a rejection as an unfinished rotation. MailMcp keeps running on what it had, so nothing breaks immediately — but the deployment is now running configuration that differs from what is on disk, and revoking the old credential at that point is what would take it offline.
