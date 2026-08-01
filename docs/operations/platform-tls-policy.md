# The platform TLS policy and legacy mail servers

Every TLS connection MailFathom makes is handshaked by the system OpenSSL, and OpenSSL refuses parameters its own
security policy considers too weak before MailFathom sees the server at all. On Ubuntu that policy is compiled in at
security level 2 — `openssl version -a` reports `-DOPENSSL_TLS_SECURITY_LEVEL=2` — which requires at least 112 bits of
security and so rejects a Diffie-Hellman group below 2048 bits, an RSA key below 2048 bits, and a SHA-1 signature.

Almost every mail server meets that. A few, still in service, do not: one offering `DHE-RSA-AES256-GCM-SHA384` over a
1024-bit group as its only cipher suite, with no ECDHE suite and no TLS 1.3, is refused by a stock Ubuntu machine
however MailFathom is configured. This page is what to do about that, and what it costs.

## What the failure looks like

Synchronization reports an authentication failure:

```text
AuthenticationException: Authentication failed, see inner exception.
  inner: SslException: SSL Handshake failed with OpenSSL error - SSL_ERROR_SSL
```

**It is not an authentication failure.** The connection never reached authentication: the handshake ended before any
credential was sent, and the message says `Authentication` because that is what .NET calls establishing a TLS session.
Nothing about the password, the `plaintext:` or `file:` secret reference behind it, or
`PermittedAuthenticationMechanisms` is involved, and changing any of them changes nothing.

## Confirming it is the handshake

Ask the server what it offers, from the same machine:

```bash
openssl s_client -connect mail.example.test:993 -brief </dev/null
```

A server the platform accepts completes and prints its protocol and cipher. One it refuses fails here too, with the
same OpenSSL error the host reported — which is the confirmation, because `openssl s_client` sends no credential at
all. Adding `-cipher 'DEFAULT@SECLEVEL=1'` to the same command and having it succeed identifies the security level as
what refused it.

## The one supported relaxation

Write a configuration file that lowers the security level to 1, and point OpenSSL at it with `OPENSSL_CONF`:

```ini
# /etc/mailfathom/openssl-legacy.cnf
openssl_conf = mailfathom_openssl_init

[mailfathom_openssl_init]
ssl_conf = mailfathom_ssl

[mailfathom_ssl]
system_default = mailfathom_system_default

[mailfathom_system_default]
CipherString = DEFAULT@SECLEVEL=1
```

The whole `openssl_conf` → `ssl_conf` → `system_default` chain has to be there. The distribution's own
`/usr/lib/ssl/openssl.cnf` configures no TLS policy at all — the level-2 default comes from the compile-time flag — so
there is no existing section to amend, and `OPENSSL_CONF` replaces that file rather than adding to it.

Security level 1 corresponds to 80 bits: it admits a 1024-bit Diffie-Hellman group, a 1024-bit RSA key, and a SHA-1
signature, and it changes nothing else. It does not lower the protocol floor, and it does not turn off certificate
validation — a server whose certificate does not verify still fails, and
[trusting a private authority](../features/imap-synchronization.md#trust-anchor-material) is the setting for that.

Measured on Ubuntu with OpenSSL 3.0.13 and .NET 10: a TLS 1.2 server offering only 1024-bit `DHE-RSA-AES256-GCM-SHA384`
is refused with the error above under the platform default, and negotiates
`Tls12 TLS_DHE_RSA_WITH_AES_256_GCM_SHA384` with this file in force.

**Check the path.** A variable naming a file that does not exist is not an error: OpenSSL falls back to its defaults
without saying so, the process starts, and the handshake fails exactly as it did before — measured, on the same
platform, against the same server. The startup warning below reports that the variable is set, which is all it can
know; it is not evidence that the file was read.

### Pointing a deployment at it

The variable has to be in the environment of the MailFathom process before it starts.

- **Locally, through Aspire.** Export it in the shell you start the orchestration from, and the app model passes it
  through to the `mailfathom-host` resource. It passes the value through rather than setting one, so a checkout that
  exports nothing runs under the platform default; the integration-test topology never receives it at all.

  ```bash
  export OPENSSL_CONF=/etc/mailfathom/openssl-legacy.cnf
  dotnet run --project src/AppHost/AppHost.csproj
  ```

- **As a native process.** Set it in the unit's own environment — `Environment=OPENSSL_CONF=/etc/mailfathom/openssl-legacy.cnf`
  in a systemd unit — rather than in a login profile, so it reaches the service and nothing else on the machine.

- **With Docker Compose.** Add the file to the service as a read-only bind mount and name it in the `environment:`
  block of the `mailfathom` service in [`deploy/compose/compose.yaml`](../../deploy/compose/compose.yaml). The
  container's own OpenSSL enforces the same policy the host's does, so the file has to be inside the container.

- **On Kubernetes.** `config.extraEnvironment` sets the variable, but the chart mounts only the JSON configuration
  directory and the secret directory, and the configuration ConfigMap rejects a file name that does not end in `.json`.
  There is currently no chart hook for an arbitrary file, so the file has to reach the container another way — an image
  built on top of the published one is the straightforward path.

## It cannot be an `appsettings.json` setting

OpenSSL initializes before .NET configuration binding runs, and it reads `OPENSSL_CONF` while initializing. A value
written into `appsettings.json`, a mounted ConfigMap, or user secrets is read long after that and is silently
ineffective — the process starts, the setting is present, and the handshake fails exactly as before.

This is why MailFathom exposes no key for it. [ADR 0002](../decisions/0002-configuration-reading-mapping-and-reload-boundary.md)
places configuration reading at the host boundary; this is one step earlier than any boundary that ADR describes, which
makes it a pre-start environment concern by nature rather than by choice.

## The scope is the whole process

`OPENSSL_CONF` is not scoped to a connection, a mail account, or a protocol. It governs every TLS session the process
takes part in:

- the IMAP connections it was set for, across every configured account rather than the one that needed it;
- the PostgreSQL connection, whenever it is encrypted;
- the cipher selection of the MCP endpoint's own HTTPS listeners, when this process terminates TLS;
- every further TLS session the process takes part in.

A weakened level accepted for one legacy mail server therefore also accepts a weaker parameter from every other peer
that offers one. That is the cost, it is not adjustable per account, and it is why the host says so at startup.

## What it does not weaken

**The MCP endpoint's TLS floor holds.** `McpEndpoint:Https:Endpoints:<n>:MinimumTlsVersion` admits only `Tls12` and
`Tls13`, and those listeners name their versions to the platform explicitly rather than inheriting a default. A
configuration file that lowers `MinProtocol` does not reach them: measured on the same platform, a peer naming
`Tls12 | Tls13` refuses a TLS 1.0 partner even with `MinProtocol = TLSv1` and security level 0 in force. What such a
file does still reach is which ciphers and key sizes those listeners accept.

**The health endpoint's TLS listener is not covered by that.** It names no version and takes the platform's, so a
lowered `MinProtocol` would lower what the probes accept as well — one more reason the supported file changes the
security level only, which leaves every protocol floor where it was.

**Certificate validation is untouched.** MailFathom cannot be configured to skip it, here or anywhere else.

## What the platform additionally permits, and why not to use it

Two further relaxations are available and neither is supported here. They are recorded so that a reader who finds them
elsewhere knows what they do:

- `CipherString = DEFAULT@SECLEVEL=0` disables the security checks rather than lowering them — any key size, any
  signature algorithm, and the export-grade and null ciphers among them. There is no server that needs it and not
  level 1.
- `MinProtocol = TLSv1` or `TLSv1.1` lowers the protocol floor below TLS 1.2. Both versions are deprecated by
  [RFC 8996](https://www.rfc-editor.org/rfc/rfc8996) and neither is a level the project supports. On this platform they
  additionally require level 0: measured, a TLS 1.0 handshake fails under `MinProtocol = TLSv1` with security level 1
  and succeeds only once the level is 0 as well, because the TLS 1.0 key exchange is signed with MD5-SHA1.

A server that needs either of those is a server to replace, and the mailbox is better reached by fixing the server than
by configuring MailFathom around it.

## The startup warning

A host started with the variable set says so, once, at `Warning`:

```text
This process was started with OPENSSL_CONF set, so its TLS parameters come from
/etc/mailfathom/openssl-legacy.cnf rather than from the platform default, and its TLS posture may be weaker than that
default. The scope is the whole process: whatever that file relaxes applies to the mail connection it was most likely
set for, and equally to the database connection and to every other TLS session this process takes part in. Unset it
once the server that needed it no longer does.
```

It names the path and never the file's contents, and it stays silent when the variable is unset or empty. The warning
is the only place the relaxation is visible from inside MailFathom: nothing about it appears in any configuration the
host reports, because OpenSSL read it before that configuration existed.

## Related

- [IMAP synchronization](../features/imap-synchronization.md#transport-security) — the transport-security settings
  MailFathom itself owns, which apply once the handshake is possible at all.
- [Configuration reference](configuration-reference.md#environment-only-settings) — every setting read from the
  environment alone.
- [MCP endpoint](mcp-endpoint.md) — the inbound TLS posture, including the minimum version this file cannot lower.
