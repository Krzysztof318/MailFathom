# Mailbox OAuth

<!-- describes: src/Infrastructure/Mail/OAuth/**, src/Cli/** -->

How a mailbox that no longer accepts a password is authenticated, and what each provider requires before it will
issue the credential MailFathom runs on.

MailFathom never obtains a refresh token while it is serving. It is a headless service, it ships in a container, and
it serves no consent page and owns no redirect endpoint — so the sign-in that produces a refresh token is an
administration act you perform once, with the `mailfathom` command, and the result is provisioned as a secret like
every other credential. The running service only ever exchanges that token for short-lived access tokens.

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
`mailfathom` command produces a refresh token, not how the service authenticates.

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
4. Register `http://localhost:8765/` as an authorized redirect URI, or whichever address you will pass to
   `--redirect-uri`. Nothing listens there; see below for why that is the point.

## Obtaining the refresh token

The command is in the published container image and in the release archive. It writes nothing: the refresh token goes
to standard output and everything else to standard error, so redirecting output captures the token alone.

### Microsoft — the device grant

Nothing on the machine running the command needs a browser.

```console
$ mailfathom mailbox authorize --provider microsoft --client-id <client-id> --mode device --public-client

Open this address on any device with a browser:
  https://microsoft.com/devicelogin

and enter the code: F7KQ-9XBM
The code expires at 2026-08-01 12:15:00Z. Waiting for the sign-in to complete...
```

Sign in on your own computer or phone, and the command completes on its own.

### Google — the manual grant

**Google's device flow cannot be used here.** Google operates one, but its allowed-scope list covers only OpenID
Connect, Drive, and YouTube scopes — no mail scope is obtainable through it. So the authorization-code grant is used,
with the redirect landing on a loopback address:

```console
$ mailfathom mailbox authorize --provider google --client-id <client-id>
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
which tries to deliver it to `http://localhost:8765/` on *your* machine, where nothing is listening — so the code
stays in the address bar and reaches the server only when you paste it. The request is bound by PKCE and by the
`state` value the command checks, so a code from a different authorization cannot be redeemed.

Run the command on a machine that does have a browser and both steps happen on one screen; the paste is what makes a
headless server workable, not what makes it awkward.

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
- **A refused grant is not retried.** An authorization server answering `invalid_grant` or `invalid_client` has
  decided, and repeating the request only spends the account's rate limit. An unreachable server is retried, bounded
  and jittered, under the `MailAuthorizationServerInvocation` resilience budget.

## Rotation

A refresh token is a long-lived credential; rotate it like any other, through
[secret rotation](secret-rotation.md). Repointing the reference is picked up by the next token request with no
restart.

**Microsoft Entra rotates the refresh token on every refresh, and MailFathom cannot follow it.** The service reads its
refresh token from a secret reference it has no write access to — deliberately, since a process that could rewrite its
own credentials could also destroy them — so it keeps using the token you provisioned. That works while the previous
token remains valid, and a rotated token arriving in a response is logged as a warning naming the account. Treat the
warning as the signal to re-run the authorization before the configured token stops being accepted.

## Troubleshooting

| What you see | What it means |
| --- | --- |
| `no_refresh_token_issued` | The grant returned an access token only. For Google, consent was not re-prompted; the command already forces it, so check that the client is a Desktop app. For Microsoft, `offline_access` is missing from the scope. |
| `invalid_grant` at startup or in a run | The refresh token is revoked, expired, or belongs to a different client. Re-run the authorization. |
| `invalid_client` | The client ID or client secret does not match the registration, or a confidential client was authorized as a public one. |
| `state_mismatch` | The pasted values came from a different authorization run. Start the command again and use one browser tab. |
| `expired_token` during the device grant | Nobody completed the sign-in before the code expired. Run the command again. |
| Startup refuses the account naming `mailfathom mailbox authorize` | The account uses the `refresh_token` grant and configures no refresh token reference. |

## Related

- [Configuration reference](configuration-reference.md) — every key in the `OAuth` block
- [Secret provisioning](secret-provisioning.md) — how a reference is backed by material
- [IMAP synchronization](../features/imap-synchronization.md) — the transport security policy the token path does not relax
