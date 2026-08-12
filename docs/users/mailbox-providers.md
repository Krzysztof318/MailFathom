# Configuring a mailbox at your provider

<!-- describes: src/Host/Configuration/Mail/**, src/Infrastructure/Mail/** -->

> [!WARNING]
> Some of the steps on this page are performed in a product this project does not control. Any screen, menu, or field
> named here can be renamed or moved there at any time. Where this page and that product's own documentation disagree,
> the product's documentation is right.

[Getting started § configure the mailbox](getting-started.md#2-configure-the-mailbox) shows the account block and what
each of its keys is for. This page answers the question that comes next and that no reference page can answer, because
the answer belongs to somebody else: **what goes in `Host`, `Port`, `Secrets`, and `TransportSecurity` for the mail
service I actually use, and what does that service do differently once synchronization is running.**

Every mail service named below is reached the same way — MailFathom speaks IMAP over TLS and nothing else, and there is
no per-provider code path anywhere in it. Four things differ, and a reader has to get each right before the first
connection succeeds: the address, whether IMAP has to be switched on first, which credential the service will accept,
and what the service's own behaviour does to a mailbox once mail starts arriving in the local copy.

## Two claims this page does not make

**Presence is a check at a point in time, not a supported-provider list.** Every entry below is what that service's own
current documentation said on the date the entry carries. None of these services is under this project's control, and
any of them may change the answer next week without anybody here touching anything. An entry that has stopped being
true is a defect in this page rather than in the deployment that trusted it.

**Absence is not a refusal.** A service missing from this page is not blocked, unsupported, or known to fail — it is
unchecked. MailFathom reaches any IMAP server that will serve a TLS connection and an authentication mechanism the
account permits, and [an IMAP server you run yourself](#an-imap-server-you-run-yourself) is the section for one.

## What the evidence column means

Each entry says which of two kinds of evidence it rests on, because they are not the same claim and blurring them is
how a guide starts lying:

- **Documented** — the service's own current documentation was read on the date the entry carries. It establishes what
  the service publishes about its address, its credential, and its own behaviour. It does not establish that a
  connection was made.
- **Observed** — a MailFathom deployment ran against a mailbox at that service and the behaviour was what the entry
  says.

**Every entry on this page today is `Documented`.** That is deliberate rather than a gap waiting to be filled: a third
party's mail server is not something this repository verifies, and an entry claiming otherwise would be asserting a test
that nothing here runs. Where a reader needs a behaviour confirmed against their own mailbox, [what your own server
actually offers](#what-your-own-server-actually-offers) is how the running deployment answers it.

## The account block, written once

This is the complete shape, and it is shown once. Every section below changes three parts of it at most — the address,
the credential block, and, in one case, `TransportSecurity`:

```json
{
  "MailSynchronization": {
    "Enabled": true,
    "Accounts": [
      {
        "AccountId": "primary",
        "DisplayName": "Personal mail",
        "Host": "imap.example.test",
        "Port": 993,
        "UserName": "you@example.test",
        "Secrets": {
          "Password": {
            "Name": "imap-primary-password",
            "SecretReference": "file:/etc/mailfathom/secrets/imap-primary-password"
          }
        },
        "Folders": [
          { "Alias": "inbox", "SpecialUse": "Inbox" },
          { "Alias": "sent", "SpecialUse": "Sent" }
        ]
      }
    ]
  }
}
```

A service that will not accept a password takes an `OAuth` block in place of `Secrets` instead;
[mailbox OAuth § configuring the account](../operations/mailbox-oauth.md#configuring-the-account) holds that form in
full, and no section below repeats it. [Configuration reference § `MailSynchronization`](../operations/configuration-reference.md#mailsynchronization)
is the inventory of every key named on this page, with its type, default, and constraint.

## What is the same everywhere

Four things hold whichever service a mailbox lives at, and each is stated in full on the page that owns it rather than
per provider here:

- **The transport is TLS on connect, on port 993, by default.** Every weakening — an unencrypted connection, clear-text
  authentication over one — is an explicit opt-in that fails startup otherwise, and certificate validation cannot be
  turned off at all. A server with a private certificate authority is reached by trusting that authority.
  [IMAP synchronization § transport security](../features/imap-synchronization.md#transport-security) records the rules.
- **Folders are named by role rather than by path.** `SpecialUse` lets discovery find a folder whatever the server calls
  it, which matters more here than it looks: the same role carries a different path at almost every service below.
  [Folder aliases and discovery](../features/imap-synchronization.md#folder-aliases-and-discovery) covers the matching.
- **Reading never marks mail read.** MailFathom does not set the remote `\Seen` flag while synchronizing, reconciling,
  fetching content, or answering a tool call — the sessions those run on hold no operation capable of writing a flag.
  If mail is turning up read at your service, it is another client or a rule at the service, not this one.
  [Marking mail read is an act, never a side effect of reading](../features/imap-synchronization.md#marking-mail-read-is-an-act-never-a-side-effect-of-reading)
  states the guarantee and its one deliberate exception.
- **Whether `"Mode": "Push"` does anything is the server's answer, not a setting.** Push needs the `IDLE` extension, and
  one connection for the whole account instead of one per folder additionally needs `NOTIFY`. A server offering neither
  is polled on the account's interval and says so in the log.
  [Push synchronization](../features/imap-synchronization.md#push-synchronization) holds the whole model.

## The addresses, in one table

| Service | `Host` | `Port` | Switch IMAP on first | Credential the service will accept | Evidence |
| --- | --- | --- | --- | --- | --- |
| [Gmail, on a personal account](#gmail-on-a-personal-account) | `imap.gmail.com` | `993` | No — always on | App password, or OAuth | Documented, 2026-08-12 |
| [Gmail, on a Google Workspace account](#gmail-on-a-google-workspace-account) | `imap.gmail.com` | `993` | Yes, by an administrator | **OAuth only** | Documented, 2026-08-12 |
| [Outlook.com](#outlookcom) | `outlook.office365.com` | `993` | Yes, in the mailbox settings | **OAuth only** | Documented, 2026-08-12 |
| [Exchange Online and Microsoft 365](#exchange-online-and-microsoft-365) | `outlook.office365.com` | `993` | No — on unless an administrator turned it off | **OAuth only** | Documented, 2026-08-12 |
| [Yahoo Mail](#yahoo-mail) | `imap.mail.yahoo.com` | `993` | Not documented as required | App password | Documented, 2026-08-12 |
| [iCloud Mail](#icloud-mail) | `imap.mail.me.com` | `993` | Not documented as required | App-specific password | Documented, 2026-08-12 |
| [Proton Mail](#proton-mail-through-the-local-bridge) | `127.0.0.1` (the local bridge) | `1143` | The bridge is installed and signed in instead | The bridge's own generated password | Documented, 2026-08-12 |
| [Fastmail](#fastmail) | `imap.fastmail.com` | `993` | Not documented as required | App password | Documented, 2026-08-12 |
| [Zoho Mail](#zoho-mail) | `imap.zoho.com` or `imappro.zoho.com` | `993` | Yes, in webmail | Account password, or an application-specific one | Documented, 2026-08-12 |

Proton Mail is the one row whose address is not the mail service and whose port is not 993, for the reason its section
gives. Everything else takes the account block above with the address and the credential replaced.

*Not documented as required* is the honest answer rather than a *no*: the service's own setup documentation names no
switch, and this review made no connection that would establish one.

## Gmail, on a personal account

Google documents `imap.gmail.com` on port `993` with SSL required, and states that IMAP access is always on and no
longer has a setting to switch — so nothing has to be enabled first on a personal account.

**The credential.** An app password works with the ordinary `Secrets:Password` block and is the shorter path; OAuth is
the alternative and is the same setup a Google Workspace mailbox needs.
[Mailbox OAuth § do you need this at all](../operations/mailbox-oauth.md#do-you-need-this-at-all) is where the choice is
already stated in full — which accounts can be issued an app password at all, and the review obligation that comes with
registering your own Google project for mail access — and it is not repeated here.

**Labels arrive as folders, and one message can be in several of them.** Google publishes labels over IMAP as folders,
with its own labels under a `[Gmail]` prefix, and exposes each message's label set through the `X-GM-LABELS` attribute.
`[Gmail]/All Mail` holds everything, so a message carrying two labels is reachable through both label folders and
through All Mail as well. Configure the folders you want by role — an inbox, a sent folder — rather than synchronizing
All Mail beside them, or the same message is copied under several aliases and counted several times in every listing.

**Two limits worth knowing before the first synchronization of a large mailbox.** Google documents a daily IMAP ceiling
of 2500 MB downloaded and 500 MB uploaded — synchronization only reads, so the download figure is the one a first pass
over a large mailbox can reach — and describes a safeguard that suspends the account for about an hour, and for as long
as a day, once a limit is hit. It separately documents that an account may be added to at most 15 mail clients at once,
which a deployment watching many folders in push mode can approach on its own. `EarliestEmailReceivedDate` is what
bounds the first pass against the first limit, and `MaxSubscribedFolders` bounds the connection count against the
second.

Sources: [Add Gmail to another email client](https://support.google.com/mail/answer/7126229),
[IMAP, POP, and SMTP](https://developers.google.com/workspace/gmail/imap/imap-smtp),
[IMAP extensions](https://developers.google.com/workspace/gmail/imap/imap-extensions),
[Gmail bandwidth limits](https://support.google.com/a/answer/1071518).

## Gmail, on a Google Workspace account

The address is the same as above. Two things are not.

**An administrator turns IMAP on first.** Google documents the setting in the admin console under **Apps → Google
Workspace → Gmail → End User Access → POP and IMAP access**, applied to the whole organization or to one organizational
unit. The same setting optionally restricts access to named OAuth client identifiers, which is worth checking if the
first connection is refused after the switch is on.

**A password will not authenticate.** Google states that as of 1 May 2025 a Workspace account no longer accepts a sign-in
from an application using a username and password, and an app password cannot be issued for one at all. The account
therefore carries an `OAuth` block and no `Secrets:Password`, with `PermittedAuthenticationMechanisms` set to the
token-bearing mechanisms — which is what frees the account from configuring a password.
[Mailbox OAuth § Google](../operations/mailbox-oauth.md#google) is the registration, and
[§ configuring the account](../operations/mailbox-oauth.md#configuring-the-account) is the block.

The label and All Mail behaviour and both limits are Gmail's and apply here unchanged.

Sources: [Turn POP & IMAP on or off for users](https://support.google.com/a/answer/105694),
[Add Gmail to another email client](https://support.google.com/mail/answer/7126229).

## Outlook.com

Microsoft documents `outlook.office365.com` on port `993` with SSL/TLS, and lists the authentication method for it as
OAuth2 and modern authentication rather than a password.

**IMAP is switched on in the mailbox.** Microsoft's own settings page states that POP and IMAP access is off by default
and is enabled under **Settings → Mail → Forwarding and IMAP**.

**A password will not authenticate, and neither will an app password.** Microsoft states that basic authentication
stopped working for these accounts on 16 September 2024 and that devices using POP or IMAP can no longer use app
passwords. So an Outlook.com mailbox takes the `OAuth` block, registered through Microsoft Entra exactly as a Microsoft
365 mailbox is; [mailbox OAuth § Microsoft — Entra](../operations/mailbox-oauth.md#microsoft--entra) is the
registration. The delegated permission is the one a personal mailbox owner can consent to themselves.

Sources: [POP, IMAP, and SMTP settings for Outlook.com](https://support.microsoft.com/en-us/office/pop-imap-and-smtp-settings-for-outlook-com-d088b986-291d-42b8-9564-9c414e2aa040),
[Modern authentication methods now needed to continue syncing Outlook email in non-Microsoft email apps](https://support.microsoft.com/en-us/support/known-issues/modern-authentication-methods-now-needed-to-continue-syncing-outlook-email-in-non-microsoft-email-ap).

## Exchange Online and Microsoft 365

The address and port are the same as Outlook.com's, and so is the answer about passwords — but for a different reason
and with a different consequence, which is why this is a section of its own.

**Basic authentication was removed rather than discouraged.** Microsoft states that it removed the ability to use basic
authentication for IMAP in Exchange Online, that no one — customer or Microsoft support — can re-enable it, and that the
same change prevents the use of app passwords. OAuth 2.0 is the only way in, and Microsoft's own guidance to application
developers is to keep the protocol and implement it.

**Both grants are available here, and they are different mailboxes' answers.** A delegated registration carrying
`IMAP.AccessAsUser.All` acts for one mailbox owner and needs a refresh token obtained once; an app-only registration
carrying `IMAP.AccessAsApp` acts for the application, needs tenant administrator consent, and uses the
`client_credentials` grant with no sign-in at all. [Mailbox OAuth § the two grants](../operations/mailbox-oauth.md#the-two-grants)
states which applies, and [§ Microsoft — Entra](../operations/mailbox-oauth.md#microsoft--entra) is the registration for
both.

**IMAP is on by default per mailbox, and an administrator can turn it off.** Microsoft documents IMAP4 as enabled when a
user mailbox is created, and both the Exchange admin center and `Set-CASMailbox` as the ways to change it — so a refused
connection against a tenant that has hardened its protocols is a mailbox setting rather than a credential fault.

Sources: [Deprecation of basic authentication in Exchange Online](https://learn.microsoft.com/en-us/exchange/clients-and-mobile-in-exchange-online/deprecation-of-basic-authentication-exchange-online),
[Managing email apps for user mailboxes](https://learn.microsoft.com/en-us/exchange/recipients-in-exchange-online/manage-user-mailboxes/managing-email-apps-for-user-mailboxes),
[Authenticate an IMAP, POP, or SMTP connection using OAuth](https://learn.microsoft.com/en-us/exchange/client-developer/legacy-protocols/how-to-authenticate-an-imap-pop-smtp-application-by-using-oauth).

## Yahoo Mail

Yahoo documents `imap.mail.yahoo.com` on port `993` with SSL required, and directs a third-party mail client at a
generated app password rather than the account password. That is the ordinary `Secrets:Password` block with the
generated value behind the reference, and nothing else about the account changes.

This review found no Yahoo documentation of IMAP connection caps, bandwidth ceilings, or the extension set the server
advertises, so this page states none — [what your own server actually offers](#what-your-own-server-actually-offers) is
where a running deployment answers that instead.

Sources: [Yahoo Mail server settings](https://help.yahoo.com/kb/SLN4075.html).

## iCloud Mail

Apple documents `imap.mail.me.com` on port `993` with SSL required, and an app-specific password rather than the account
password.

**The user name is the part that catches people out.** Apple documents it as usually the name portion of the address —
`johnappleseed` rather than `johnappleseed@icloud.com` — and says to try the full address if a client cannot connect
with the short form. `UserName` is an identifier rather than a secret, so trying the other form is an edit and a reload
rather than a re-provisioning.

Sources: [iCloud Mail server settings for other email client apps](https://support.apple.com/en-us/102525).

## Proton Mail, through the local bridge

This is the one entry whose address is not the mail service. Proton documents that a mail client reaches a Proton
mailbox through Proton Mail Bridge, an application that runs on the same machine as the client, holds the connection to
Proton itself, and serves IMAP on a loopback address — which is what lets a client see decrypted mail without the
decryption happening anywhere else. Proton states that the bridge is available only with a paid Proton Mail plan.

Three consequences for a MailFathom account, and the first is the one to settle before writing any configuration:

- **The bridge has to be reachable from the process, on loopback.** A deployment in a container or on another host does
  not reach a bridge running on somebody's laptop, and the bridge is not a service to publish onto a network — its whole
  design is that the decrypted channel never leaves the device. A Proton mailbox therefore fits a MailFathom process
  running on the same machine as the bridge, and does not fit the container and Kubernetes shapes without putting the
  two together deliberately.
- **The port and the transport are not the defaults.** Proton documents the bridge's default IMAP port as `1143` and
  offers a choice of STARTTLS or SSL in the bridge's own settings. STARTTLS on 1143 means the account states
  `"ConnectionSecurity": "StartTlsRequired"` — which is one of the two guaranteed-TLS modes and needs no
  `AllowInsecureConnection`, because the handshake either happens or the connection fails.
- **The certificate is the bridge's own.** Proton documents the bridge as using a self-signed TLS certificate that it
  generates when the application is first set up. Certificate validation is never disabled in MailFathom, so the account
  trusts that certificate as an additional authority: `"CertificateTrust": "AdditionalTrustedAuthority"` with the
  bridge's certificate behind `TrustedCertificateAuthority`.
  [Trust anchor material](../features/imap-synchronization.md#trust-anchor-material) is what that reference holds and how
  it is provisioned.

The credential is the password the bridge itself generates for the mail client, with the Proton address as `UserName`.
It goes behind a `Secrets:Password` reference like any other.

```json
{
  "Host": "127.0.0.1",
  "Port": 1143,
  "UserName": "you@proton.me",
  "TransportSecurity": {
    "ConnectionSecurity": "StartTlsRequired",
    "CertificateTrust": "AdditionalTrustedAuthority",
    "TrustedCertificateAuthority": {
      "Name": "proton-bridge-certificate",
      "SecretReference": "file:/etc/mailfathom/secrets/proton-bridge-certificate.pem"
    }
  }
}
```

Sources: [IMAP, SMTP, and POP3 setup](https://proton.me/support/imap-smtp-and-pop3-setup),
[Comprehensive guide to Bridge settings](https://proton.me/support/comprehensive-guide-to-bridge-settings),
[Proton Mail Bridge connection issues](https://proton.me/support/bridge-ssl-connection-issue).

## Fastmail

Fastmail documents `imap.fastmail.com` on port `993` with SSL/TLS encryption and states explicitly that STARTTLS is not
offered there — which is what MailFathom's default `TlsOnConnect` already is, so the account needs no
`TransportSecurity` block at all.

**The user name is the full address, and it is the one the account was signed up with.** Fastmail documents that other
addresses on the same account do not authenticate.

**An app password is required.** Fastmail states that the ordinary account password will not connect over IMAP and that
each connection needs an app password of its own.

Sources: [Server names and ports](https://www.fastmail.help/hc/en-us/articles/1500000278342-Server-names-and-ports),
[IMAP, POP, and SMTP](https://www.fastmail.help/hc/en-us/articles/1500000279921-IMAP-POP-and-SMTP).

## Zoho Mail

Zoho documents two hostnames on port `993` with SSL required, and which one applies is the account's plan rather than a
preference: `imap.zoho.com` for a personal account on a `zoho.com` address, and `imappro.zoho.com` for a paid
organization account on its own domain. Zoho runs mailboxes in several data centres and the documented hostnames are the
ones for its `.com` domain, so an account elsewhere takes the host its own webmail settings page names rather than the
one in the table above.

**IMAP is switched on in webmail first.** Zoho documents enabling IMAP access for the account before configuring any
client.

**Which credential depends on how the account signs in.** Zoho documents the account password for an ordinary account,
and an application-specific password where two-factor authentication is on, where the account signs in through SAML, or
where it uses a federated sign-in. Either way it is the `Secrets:Password` block.

Sources: [IMAP and SMTP configuration details](https://www.zoho.com/mail/help/imap-access.html).

## An IMAP server you run yourself

There is no entry to look up here, and that is the point: MailFathom reaches any IMAP server that will serve a TLS
connection and an authentication mechanism the account permits, so the server's own documentation is the source and the
account block at the top of this page is the whole configuration. Four things are worth checking against it, because
each is a startup refusal rather than a runtime warning:

- **The port decides nothing; the mode does.** `993` with `TlsOnConnect` is the default. A server serving IMAP on `143`
  and upgrading takes `"ConnectionSecurity": "StartTlsRequired"`, which is still guaranteed TLS. `StartTlsWhenAvailable`
  and `None` are not, and each needs `AllowInsecureConnection` written explicitly.
- **A private certificate authority is trusted rather than ignored.** `"CertificateTrust": "AdditionalTrustedAuthority"`
  with the authority behind `TrustedCertificateAuthority`; validation itself cannot be turned off.
- **An old server may be refused before a credential is ever sent.** An authentication failure wrapping
  `SSL Handshake failed with OpenSSL error` is the platform's TLS policy ending the handshake rather than a wrong
  password. [The platform TLS policy](../operations/platform-tls-policy.md) covers confirming that and the one supported
  way to relax it.
- **The mechanism list is an allow-list.** `PermittedAuthenticationMechanisms` defaults to `PLAIN` and `LOGIN`; a server
  offering something else needs it named.

## What your own server actually offers

Nothing above states which IMAP extensions a service advertises, because almost none of them publishes that and a table
of guesses would be worse than no table. The running deployment answers it instead, per folder, on every start:

```text
Folder primary/inbox is now synchronized in Push mode.
```

Three neighbouring lines say why, when the answer is not the one that was configured:

| What the log says | What the server advertised |
| --- | --- |
| `Account … watches N folders through one push subscription.` | `NOTIFY` and `IDLE`, so the whole account costs one connection |
| `… advertises no NOTIFY capability, so each push folder is watched over its own connection` | `IDLE` alone, so each watched folder costs one |
| `… the mail server advertises no IDLE capability; it is synchronized by polling` | Neither, so `Mode` changes nothing and the interval is what runs |

The same holds for `CONDSTORE` and `QRESYNC`, which decide how much work a reconciliation pass does rather than whether
it is correct: all three shapes of the question reach the same end state, and
[asking only about what changed](../features/imap-synchronization.md#asking-only-about-what-changed) records which
command each server gets. There is nothing to configure and nothing to check — a server that supports neither is slower
and no less right.

## Related

- [Getting started](getting-started.md) — the whole path from an installed instance to a first tool call
- [Mailbox OAuth](../operations/mailbox-oauth.md) — registering an application, obtaining a refresh token, and the
  `OAuth` block every token-bearing account above takes
- [Configuration reference § `MailSynchronization`](../operations/configuration-reference.md#mailsynchronization) —
  every key named here, with its constraint and whether changing it needs a restart
- [IMAP synchronization](../features/imap-synchronization.md) — what a run actually does with a mailbox
- [Secret provisioning](../operations/secret-provisioning.md) — how a `SecretReference` is backed by material

---

**Trademarks.** The product, service, and company names on this page are their owners' trademarks and are used solely to
identify the mail services a MailFathom deployment can be configured against. Their use implies no affiliation with,
sponsorship by, endorsement by, or certification from those owners, in either direction, and this page reproduces no
third-party logo.

Gmail and Google Workspace are trademarks of Google LLC. Microsoft, Outlook, Microsoft 365, Exchange Online, and
Microsoft Entra ID are trademarks of the Microsoft group of companies. iCloud is a trademark of Apple Inc., registered
in the U.S. and other countries and regions. Yahoo and Yahoo Mail are trademarks of Yahoo Inc. and its affiliate
companies. Proton and Proton Mail are trademarks of Proton AG. Fastmail is a trademark and service mark of Fastmail Pty
Ltd. Zoho and Zoho Mail are
trademarks of Zoho Corporation Private Limited and/or its affiliates.
[`THIRD_PARTY_LICENSES.md`](https://github.com/Krzysztof318/MailFathom/blob/main/THIRD_PARTY_LICENSES.md#trademark-and-brand-use)
records the per-owner review this statement comes out of, and why it sits here rather than in `NOTICE`.
