# The platform TLS policy and legacy mail servers

<!-- describes: deploy/openssl/**, backend/src/Infrastructure/Mail/**, backend/src/Infrastructure/Certificates/** -->

Every TLS connection MailFathom makes is handshaked by the system OpenSSL, and OpenSSL refuses parameters its own
security policy considers too weak before MailFathom sees the server at all. On Ubuntu that policy is compiled in at
security level 2 — `openssl version -a` reports `-DOPENSSL_TLS_SECURITY_LEVEL=2` — which requires at least 112 bits of
security and so rejects a Diffie-Hellman group below 2048 bits, an RSA key below 2048 bits, and a SHA-1 signature.

Almost every mail server meets that. A few, still in service, do not: one offering `DHE-RSA-AES256-GCM-SHA384` over a
1024-bit group as its only cipher suite, with no ECDHE suite and no TLS 1.3, is refused by a stock Ubuntu machine
however MailFathom is configured. This page is what to do about that, and what it costs.

## Everything here is opt-in

**Nothing on this page is on by default, and no default in this repository moves because it exists.** A MailFathom
that names no OpenSSL configuration file negotiates the strongest protocol and cipher suite the two ends agree on —
TLS 1.3 wherever the server supports it — under the platform's own full-strength policy, and refuses anything that
policy considers weak. That is what a checkout, a container, and a published artifact all do out of the box.

The mechanism below changes that for one process, only when an operator deliberately sets one environment variable,
and it stays visible while it is set: the host says so at startup, every time it starts. A deployment that never meets
a legacy mail server never touches any of it, and reading this page is not a step in an ordinary installation.

## Which OpenSSL

The whole page assumes Linux, because [that is the only platform this project officially supports](../users/installation.md#what-every-shape-needs)
and it is where .NET hands the handshake to OpenSSL at all. Running on Windows may work and is not verified here;
`OPENSSL_CONF` means nothing there, and so does everything below it.

**MailFathom supports OpenSSL 3.0 and later**, which is what every current distribution ships and what everything
below is measured against.

**1.1.1 is a hard floor set by .NET rather than by this project.** .NET 10 requires OpenSSL 1.1.1 or later on Unix and
[fails to start](https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/10.0/openssl-version-requirement)
without it, so a machine below that never reaches a handshake to fail.

**Between the two, MailFathom may work and may not, and that is not a promise this project can make.** The mechanism
this page uses is old enough — security levels and the `@SECLEVEL` cipher-string keyword arrived in
[OpenSSL 1.1.0](https://docs.openssl.org/1.1.1/man3/SSL_CTX_set_security_level/) — but 1.1.1 left upstream support in
September 2023, nothing here is verified against it, and its own defaults differ from 3.x's. Treat a failure that
reproduces only on such a machine as an environment to upgrade rather than as a defect to report.

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

Copy the sample this repository ships and point `OPENSSL_CONF` at the copy:

```bash
cp deploy/openssl/legacy-mail-server.cnf.example /etc/mailfathom/openssl-legacy.cnf
```

[`deploy/openssl/legacy-mail-server.cnf.example`](https://github.com/Krzysztof318/MailFathom/blob/main/deploy/openssl/legacy-mail-server.cnf.example) is that file
with its reasoning in comments. Stripped to what it configures, it is:

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

### Pointing the host at it

**The variable has to be in the environment of the MailFathom host process itself.** That process is what opens the
IMAP connection, so nothing else needs it and nothing else can supply it after the fact: an orchestrator, a unit file,
or a container runtime matters here only as the thing that hands the host its environment. Every shape below is
therefore the same act written for a different launcher, and each is equally supported.

- **The host, run directly.** The process inherits the environment it was started in, so exporting the variable — or
  prefixing the command with it — is the whole mechanism, whether the host is run from the repository or from a publish
  output.

  ```bash
  OPENSSL_CONF=/etc/mailfathom/openssl-legacy.cnf dotnet run --project backend/src/Host/Host.csproj
  OPENSSL_CONF=/etc/mailfathom/openssl-legacy.cnf dotnet /opt/mailfathom/MailFathom.Host.dll
  ```

  The startup warning below is how to confirm the host actually received it.

- **As a systemd service.** Put it in the unit rather than in a login profile, so it reaches the service and nothing
  else on the machine. The service account has to be able to read the file, which is worth checking against whatever
  sandboxing directives the unit carries.

  ```ini
  [Service]
  Environment=OPENSSL_CONF=/etc/mailfathom/openssl-legacy.cnf
  ```

- **In the container.** The container's own OpenSSL enforces the same policy the machine's does, so the file has to be
  inside the container: mount it read-only and name it in the environment. For the Compose deployment that is one
  entry added to each of the two blocks the `mailfathom` service in
  [`deploy/compose/compose.yaml`](https://github.com/Krzysztof318/MailFathom/blob/main/deploy/compose/compose.yaml) already has:

  ```yaml
      environment:
        OPENSSL_CONF: /etc/mailfathom/openssl-legacy.cnf
      volumes:
        - type: bind
          source: ./openssl-legacy.cnf
          target: /etc/mailfathom/openssl-legacy.cnf
          read_only: true
  ```

  The repository's `.gitignore` does not cover that source path, so a Compose deployment run out of a clone shows the
  copied file as untracked.

- **On Kubernetes.** `config.extraEnvironment` sets the variable, but the chart mounts only the JSON configuration
  directory and the secret directory, and the configuration ConfigMap rejects a file name that does not end in `.json`.
  There is currently no chart hook for an arbitrary file, so the file has to reach the container another way — an image
  built on top of the published one is the straightforward path.

- **Through Aspire, locally.** The AppHost starts the host as a child resource, which inherits nothing of the kind on
  its own, so it passes the variable through from the shell the orchestration was started in. It passes a value through
  rather than setting one, so a checkout that exports nothing runs under the platform default; the integration-test
  topology never receives it at all.

  ```bash
  export OPENSSL_CONF=/etc/mailfathom/openssl-legacy.cnf
  dotnet run --project backend/src/AppHost/AppHost.csproj
  ```

## It cannot be an `appsettings.json` setting

OpenSSL initializes before .NET configuration binding runs, and it reads `OPENSSL_CONF` while initializing. A value
written into `appsettings.json`, a mounted ConfigMap, or user secrets is read long after that and reaches nothing — the
setting would be present, and the handshake would fail exactly as before.

Which is why writing one there **fails startup** naming the variable rather than being accepted. The failure that
reports it, and the other environment-only families it covers, are in
[environment-only settings](configuration-reference.md#environment-only-settings). Set it the way the deployment shapes
above set it, and confirm it arrived through the startup warning below.

This is also why MailFathom exposes no key for it. [ADR 0002](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0002-configuration-reading-mapping-and-reload-boundary.md)
places configuration reading at the host boundary; this is one step earlier than any boundary that ADR describes, which
makes it a pre-start environment concern by nature rather than by choice.

## The scope is the whole process

`OPENSSL_CONF` is not scoped to a connection, a mail account, or a protocol. It governs every TLS session the process
takes part in:

- the IMAP connections it was set for, across every configured account rather than the one that needed it;
- the PostgreSQL connection, whenever it is encrypted;
- the cipher selection of the MCP endpoint's own HTTPS listeners, when this process terminates TLS;
- the connections to the S3-compatible endpoint, where a deployment stores message payloads there;
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

**A private authority is supported instead, and one rule serves every outbound peer.** Where a server's certificate was
signed by an authority the platform's trust store does not carry, the deployment supplies that authority and the chain
is rebuilt against it: the certificate must chain to it, must carry the server-authentication extended key usage, and
must still match the name — a private authority is never a licence to accept a certificate issued for another host, and
nothing downloads an intermediate or checks revocation during the rebuild. Which authority signed a certificate is not a
question a mail server and an object store answer differently, so both reach the same rule: `MailSynchronization`'s own
[trust anchor material](../features/imap-synchronization.md#trust-anchor-material) for a mailbox, and
[`ContentStorage:ObjectStorage:TrustAnchor`](configuration-runtime.md#contentstorage) for the object-storage endpoint.
The object-storage anchor is loaded once while the host starts rather than per handshake, because the decision is a
synchronous callback inside a pooled TLS handler; replacing it is a restart, and a reference that cannot be loaded fails
startup naming the key rather than failing a handshake per request afterwards.

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
