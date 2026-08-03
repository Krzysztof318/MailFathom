# Mailbox OAuth

<!-- describes: src/Infrastructure/Mail/OAuth/**, src/Common/MailboxOAuth/**, src/Cli/**, src/Application/Accounts/**, src/Infrastructure/Persistence/Accounts/** -->

How a mailbox that no longer accepts a password is authenticated, and what each provider requires before it will
issue the credential MailFathom runs on.

MailFathom never obtains a refresh token while it is serving. It is a headless service, it ships in a container, and
it serves no consent page and owns no redirect endpoint — so the sign-in that produces a refresh token is an
administration act you perform once, with the `mfctl` command, and the result is provisioned as a secret like
every other credential. The running service only ever exchanges that token for short-lived access tokens.

**Run the command on your own computer.** It administers a deployment over HTTP and needs nothing from the machine the
service runs on, which is what makes the ordinary browser sign-in available: the command listens on a loopback address,
your browser delivers the authorization code to it, and there is nothing to copy. The two other modes below exist for
the machines where that is not possible.

## Do you need this at all

**A personal Gmail mailbox does not.** Google turned off basic authentication for IMAP on 14 March 2025, but app
passwords are the stated exception, and an app password works with the ordinary `Secrets:Password` block. It requires
2-Step Verification, and it is unavailable to organization accounts, to an account whose only second factor is a
security key, and to an account under Advanced Protection.

**A Google Workspace mailbox does need this**, because an app password cannot be issued for one.

**An Exchange Online mailbox needs this**, because Microsoft accepts no basic authentication for IMAP at all.

Weigh the Gmail obligation before you start: IMAP access needs the `https://mail.google.com/` scope, Google classifies
it as restricted, and production use requires a CASA Tier 2 assessment by an approved lab in addition to Google's own
app review, with reverification at least every twelve months. In a self-hosted deployment where you register your own
Google project, that obligation is yours.

## The two grants

| | `refresh_token` | `client_credentials` |
| --- | --- | --- |
| Acts for | one mailbox owner | the registered application |
| Needs a person to sign in | once, to produce the refresh token | never |
| Where it applies | Google, and Microsoft delegated access | Exchange Online app-only access |

The authorization-code and device-code grants appear nowhere in the service's configuration. They are how the
`mfctl` command produces a refresh token, not how the service authenticates.

## Registering the application

### Microsoft — Entra

1. Register an application in Microsoft Entra ID.
2. Add the delegated permission `IMAP.AccessAsUser.All` under **Office 365 Exchange Online**, and `SMTP.Send` if the
   deployment will send mail. App-only access uses `IMAP.AccessAsApp` instead and needs tenant administrator consent.
3. Enable the public client flow if you will authorize with the device grant and registered no client secret.
4. Note the application (client) ID. Substitute your tenant identifier for `common` in the endpoints if the
   deployment is restricted to one tenant.

### Google

1. Create a project in the Google Cloud console and enable the Gmail API.
2. Configure the OAuth consent screen and add the `https://mail.google.com/` scope. This is the restricted scope whose
   assessment is described above.
3. Create an OAuth client of type **Desktop app**, and note the client ID and client secret.
4. Register `http://127.0.0.1:8765/` as an authorized redirect URI, or whichever address you will pass to
   `--redirect-uri`. The literal address rather than `localhost`, because a name resolving to both an IPv4 and an
   IPv6 address gives the browser two places to deliver the code and the command one to listen on.

## Obtaining the refresh token

The command is in the published container image and in the release archive. It writes nothing: the refresh token goes
to standard output and everything else to standard error, so redirecting output captures the token alone.

Three modes, and the first is the one to reach for:

| Mode | When | What you do |
| --- | --- | --- |
| `interactive` (default) | The command runs where a browser can reach `127.0.0.1` | Approve in the browser. Nothing to copy. |
| `device` | Microsoft, on a machine with no browser at all | Type a short code on your phone. |
| `manual` | Google, on a machine with no browser | Open a printed address elsewhere, paste two values back. |

### The interactive sign-in

```console
$ mfctl mailbox authorize --provider google --client-id <client-id>
Client secret (leave empty for a public client):

A browser has been opened for you. If it did not appear, open this address yourself:

  https://accounts.google.com/o/oauth2/v2/auth?client_id=…&code_challenge=…

Waiting for the sign-in to come back to http://127.0.0.1:8765/...
```

The command binds `http://127.0.0.1:8765/` before it shows the address, so approving quickly cannot outrun it. The
authorization server redirects your browser there, the code arrives without crossing a network, the browser is answered
with a page telling you to return to the terminal, and the listener stops. Register that address as a redirect URI with
the provider, or pass a different one with `--redirect-uri`; it must be a loopback address, and a routable one is
refused rather than bound.

**Running the command on a headless server is your problem to solve, and it is one command.** Forward the port from the
machine that has the browser:

```console
$ ssh -L 8765:127.0.0.1:8765 operator@mail.example.test
```

Then run the command over that session. The address it prints opens in the browser on your own machine, and the
redirect travels back down the forwarded port to the listener on the server. Nothing about MailFathom knows the
difference. If you would rather not forward a port, use `--mode device` for Microsoft or `--mode manual` for Google.

### Microsoft — the device grant

Nothing on the machine running the command needs a browser.

```console
$ mfctl mailbox authorize --provider microsoft --client-id <client-id> --mode device --public-client

Open this address on any device with a browser:
  https://microsoft.com/devicelogin

and enter the code: F7KQ-9XBM
The code expires at 2026-08-01 12:15:00Z. Waiting for the sign-in to complete...
```

Sign in on your own computer or phone, and the command completes on its own.

### Google — the manual grant

**Google's device flow cannot be used here.** Google operates one, but its allowed-scope list covers only OpenID
Connect, Drive, and YouTube scopes — no mail scope is obtainable through it. So Google always uses the
authorization-code grant, and `--mode manual` is what runs it without a listener:

```console
$ mfctl mailbox authorize --provider google --client-id <client-id> --mode manual
Client secret (leave empty for a public client):

Open this address in a browser, on any computer:

  https://accounts.google.com/o/oauth2/v2/auth?client_id=…&code_challenge=…

After you approve access the browser is redirected to the registered address and will
most likely show a connection error. That is expected: nothing is listening there, and
the authorization code never leaves your machine. Copy the value of the 'code' query
parameter out of the address bar and paste it below.

The 'state' parameter from the same address:
Authorization code:
```

The failed redirect is the mechanism rather than a defect. The authorization server hands the code to *your* browser,
which tries to deliver it to `http://127.0.0.1:8765/` on *your* machine, where nothing is listening — so the code
stays in the address bar and reaches the server only when you paste it. The request is bound by PKCE and by the
`state` value the command checks, so a code from a different authorization cannot be redeemed.

This is the last resort. The default mode does listen at that address, which removes both pastes.

## Configuring the account

Provision the refresh token and the client secret through [secret provisioning](secret-provisioning.md), then point
the account at them. The permitted mechanisms are what switch the account onto the token path.

```jsonc
{
  "MailSynchronization": {
    "Accounts": [
      {
        "AccountId": "workspace",
        "Host": "imap.gmail.com",
        "Port": 993,
        "UserName": "mailbox@example.com",
        "TransportSecurity": {
          "PermittedAuthenticationMechanisms": ["XOAUTH2", "OAUTHBEARER"]
        },
        "OAuth": {
          "Grant": "refresh_token",
          "TokenEndpoint": "https://oauth2.googleapis.com/token",
          "ClientId": "…apps.googleusercontent.com",
          "Scope": "https://mail.google.com/",
          "ClientSecret": { "SecretReference": "systemd-credential:workspace-oauth-client-secret" },
          "RefreshToken": { "SecretReference": "systemd-credential:workspace-oauth-refresh-token" }
        }
      }
    ]
  }
}
```

An application registered as a public client — which is what the Microsoft device grant expects and what
`--public-client` authorizes against — declares `"PublicClient": true` and configures no `ClientSecret` at all.
Configuring both is refused rather than ignored, because one of the two states then describes something the account
will not do.

No `Secrets:Password` block appears, and none is wanted: an allow-list that is entirely token-bearing frees the
account from configuring one. Keeping a password mechanism in the list means the account still needs a password, which
is the supported way to keep a working credential while a token is being provisioned.

Startup refuses, rather than warns about, an OAuth block on an account whose mechanisms could never use it, a token
endpoint that is not absolute HTTPS, and a `refresh_token` grant with no refresh token reference.

## What the service does with it

- **One token per account, shared.** An access token is cached process-wide and reused by every folder and worker on
  that account, so a synchronization run costs one token request rather than one per connection.
- **Refreshed before it expires.** A token within a minute of expiry is replaced rather than presented.
- **Which mechanism is the server's choice.** The advertised capabilities decide between `OAUTHBEARER` and `XOAUTH2`;
  where a server offers both, the registered `OAUTHBEARER` is used.
- **A rejected token is retried once, with a new one.** A token the mail server refuses despite being unexpired — it
  was revoked, or the mailbox password changed — triggers one renewal and one re-authentication. A fresh token that is
  also refused fails the attempt rather than looping.
- **The stored refresh token is preferred over the configured one.** An account whose token has been rotated at least
  once spends what MailFathom stored; one whose token never has is served from its configured reference. Both are
  handled the same way from there.
- **A refused grant is not retried.** An authorization server answering `invalid_grant` or `invalid_client` has
  decided, and repeating the request only spends the account's rate limit. An unreachable server is retried, bounded
  and jittered, under the `MailAuthorizationServerInvocation` resilience budget.

## Rotation

**The refresh token an authorization server issues is followed.** Microsoft Entra replaces the refresh token on every
refresh and invalidates the one it replaces; MailFathom stores the new one and spends it on the next request, so the
account keeps working without anybody re-running the authorization. The configured reference is the seed: it is read
while no token has been stored, and stops being read once one has.

The stored token is held in the database, sealed under the deployment's data-encryption key — see
[secret provisioning](secret-provisioning.md) for the key and
[the configuration reference](configuration-reference.md#dataencryption) for its section. An account whose deployment
configures no key ring can still authenticate from its configured reference; it is the moment a rotation arrives that
needs the key, and without one the rotation is logged as an error and the account keeps running on the token it
already had.

A refresh token is still a long-lived credential you can rotate deliberately, through
[secret rotation](secret-rotation.md). Repointing the reference is picked up by the next token request with no restart
— unless a token has already been stored for that account, in which case the stored one wins and the way to replace it
is to re-run the authorization.

Two failures are worth knowing about, because neither is silent and both end the same way. If MailFathom cannot write
the rotated token — the database is unreachable, the key ring is gone — the token request still succeeds and the
failure is logged as an error naming the account; the account keeps working until the token it replaced stops being
accepted. If the process stops between receiving a rotated token and storing it, that rotation is lost. In both cases
the account eventually answers `invalid_grant`, and the repair is to run `mfctl mailbox authorize` again.

## Troubleshooting

| What you see | What it means |
| --- | --- |
| `no_refresh_token_issued` | The grant returned an access token only. For Google, consent was not re-prompted; the command already forces it, so check that the client is a Desktop app. For Microsoft, `offline_access` is missing from the scope. |
| `invalid_grant` at startup or in a run | The refresh token is revoked, expired, or belongs to a different client. Re-run the authorization. A rotation MailFathom could not store reaches you this way as well; the error logged when the store failed says so. |
| `The rotated refresh token … could not be stored` | The database was unreachable, or the key ring the value seals under is not configured. The account keeps working until the previous token stops being accepted, so fix the cause and it recovers on the next rotation. |
| `The data-encryption key ring configures no key` | A stored token names a key the ring no longer holds. Restore that key entry; a stored value cannot be opened without it. |
| `invalid_client` | The client ID or client secret does not match the registration, or a confidential client was authorized as a public one. |
| `state_mismatch` | The redirect came from a different authorization run. Start the command again and use one browser tab. |
| `access_denied` | The sign-in was refused at the consent screen, or the account is not permitted to grant the scope. |
| `no_authorization_code` | The redirect carried neither a code nor an error, which is what a stray request to the listener looks like. Start the command again. |
| `is not a loopback address` | `--redirect-uri` named an address this machine cannot receive a redirect on. Use `http://127.0.0.1:<port>/`, or `--mode manual`. |
| `Nothing can listen at http://127.0.0.1:…` | Another program holds the port. Pass a different `--redirect-uri` and register it with the provider. |
| `expired_token` during the device grant | Nobody completed the sign-in before the code expired. Run the command again. |
| Startup refuses the account naming `mfctl mailbox authorize` | The account uses the `refresh_token` grant and configures no refresh token reference. |
| Startup refuses the account asking for a client secret | The application is registered as a public client but the account does not say so. Add `"PublicClient": true`. |

## Related

- [Configuration reference](configuration-reference.md) — every key in the `OAuth` block
- [Secret provisioning](secret-provisioning.md) — how a reference is backed by material
- [IMAP synchronization](../features/imap-synchronization.md) — the transport security policy the token path does not relax
