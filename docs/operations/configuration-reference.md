# Configuration reference

<!-- describes: backend/src/**/*Options.cs, backend/src/Host/Configuration/EnvironmentOnlySettings.cs -->

Every user-settable option, checked against the options classes that bind it. Each section's table states the key, its
type, the value a deployment gets by writing nothing, the constraint startup enforces, and what a change needs to take
effect. The prose around a setting group — what it means, why it is shaped that way, how to choose a value — lives on
the page each section links; these pages are the inventory.

The inventory is four pages rather than one, grouped by what a setting is about. This page holds what is true of every
table on all four, the map to them, and the settings that are read from the environment and from nowhere else.

## Where each section lives

| Page | Sections |
| --- | --- |
| [Mail configuration](configuration-mail.md) | [`MailSynchronization`](configuration-mail.md#mailsynchronization), [`MailDelivery`](configuration-mail.md#maildelivery), [`MailboxSearch`](configuration-mail.md#mailboxsearch), [`EmailContent`](configuration-mail.md#emailcontent), [`MailRules`](configuration-mail.md#mailrules) |
| [AI configuration](configuration-ai.md) | [`SensitiveContent`](configuration-ai.md#sensitivecontent), [`SpamClassification`](configuration-ai.md#spamclassification), [`Embeddings`](configuration-ai.md#embeddings), [`Chat`](configuration-ai.md#chat), [`MailAnswering`](configuration-ai.md#mailanswering), [`EmbeddingBackfill`](configuration-ai.md#embeddingbackfill), [`MailExtractionBackfill`](configuration-ai.md#mailextractionbackfill) |
| [Endpoint configuration](configuration-endpoints.md) | [where each surface is served](configuration-endpoints.md#where-each-surface-is-served), [`ReverseProxy`](configuration-endpoints.md#reverseproxy), [`ConnectionLimits`](configuration-endpoints.md#connectionlimits), [`McpEndpoint`](configuration-endpoints.md#mcpendpoint), [`AdminEndpoint`](configuration-endpoints.md#adminendpoint), [`ClientEndpoint`](configuration-endpoints.md#clientendpoint), [`HealthEndpoints`](configuration-endpoints.md#healthendpoints) |
| [Storage, keys, jobs, and logging](configuration-runtime.md) | [`ConfigurationSources`](configuration-runtime.md#configurationsources), [`Accounts`](configuration-runtime.md#accounts), [`Secrets`](configuration-runtime.md#secrets), [`Persistence`](configuration-runtime.md#persistence-and-the-connection-string), [`ContentStorage`](configuration-runtime.md#contentstorage), [`DataEncryption`](configuration-runtime.md#dataencryption), [`Deployment`](configuration-runtime.md#deployment), [`Jobs`](configuration-runtime.md#jobs), [`Resilience`](configuration-runtime.md#resilience), [`Logging`](configuration-runtime.md#logging) |

One setting group is not a section of configuration at all. A grant names capabilities this repository publishes rather
than values a section defines, and it is written in two places: `Permissions` on an `AdminEndpoint:Authentication` entry
for the deployment's own credential, and `mfctl credential create --permission` on a credential belonging to an owner.
[What a credential may do](permissions.md) states the whole of it: the names, what each one reaches, where each grant is
written, and what a caller the grant does not admit is told.

## How to read the tables

**Keys.** Written in configuration-section form. As an environment variable, `:` becomes `__` and a list index is a
numbered segment: `MailSynchronization:Accounts:0:Host` is `MailSynchronization__Accounts__0__Host`. Where the
configuration comes from, and which source wins, is [configuration sources](configuration-sources.md).

**Types.** A `TimeSpan` binds from `hh:mm:ss` (`"00:05:00"` is five minutes; a leading `d.` adds days). A date binds
as `yyyy-MM-dd`, an instant as ISO 8601 with an explicit offset. An enum binds by member name, and a **secret block**
is the three-field shape [secret provisioning](secret-provisioning.md#the-secret-block) defines:

```json
{ "Name": "imap-primary-password", "SecretReference": "file:/etc/mailfathom/secrets/imap-primary-password", "Lifetime": "NoLimit" }
```

`Name` is the identity diagnostics use, `SecretReference` is `<scheme>:<target>` with the schemes
`systemd-credential:`, `file:`, `env:`, `database:`, and `plaintext:`, and `Lifetime` is `NoLimit` (the default) or the ISO 8601
instant the material stops being accepted. Trust-anchor and certificate blocks nest a fourth field, `Password`, itself
a secret block, for protected PKCS#12 bundles.

**Change.** What ADR 0002 classifies for the group:

- *restart* — the section is read while the host composes itself; edit it, then restart.
- *reload* — a changed value is validated and, if sound, adopted by the next operation without a restart; a rejected
  candidate leaves the running configuration in force. Reload of a file-shaped source has caveats of its own under
  Kubernetes — see [configuration sources](configuration-sources.md#reload).

Whatever the classification, the **material behind a secret reference is read per use**: rotating a password, key, or
certificate behind an unchanged reference needs no restart and no reload. [Secret rotation](secret-rotation.md) walks
each case.

**Validation.** Every MailFathom section on those pages is bound strictly: a key the section does not define fails
startup naming it, so a typo cannot silently leave a default in force. Values are validated on start, and a violated
constraint fails startup with the configuration path in the message. The two exceptions are the framework-shaped
entries — `Logging` and `ConnectionStrings` — and the single-key `Secrets:Interpretation`, which is read with a
default rather than bound as a section.

**Against what.** These four pages are written for a reader, and a generated record sits beside them for a reviewer:
`backend/tests/PublicSurfaces.UnitTests/configuration-keys.txt` carries every key the host binds, the type it binds as, and
whether the section is refused without it, rendered from the options classes themselves. It is the mechanical half of
the same inventory — no defaults, no constraints, no prose — and it exists so that a key renamed, retyped, or removed
appears as a diff in the pull request that did it. Where the two disagree, the generated file is what the code does and
the table is what needs fixing.

## Environment-only settings

A few settings are read from the environment alone, because they configure the process before configuration exists or
belong to the platform rather than to MailFathom:

| Variable | What it does |
| --- | --- |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Attaches the OTLP exporter for logs, metrics, and traces — startup records included. Unset exports nothing. [Telemetry](telemetry.md) is the page, including the sibling `OTEL_*` variables the exporter reads itself |
| `OTEL_SERVICE_NAME` | The service identity the startup records and every exported record carry. Unset reports the host assembly's own name |
| `OTEL_TRACES_SAMPLER` / `OTEL_TRACES_SAMPLER_ARG` | How much of a trace is recorded. Unset records every trace this process starts and honors the decision on one it did not; [telemetry](telemetry.md#how-much-of-a-trace-is-recorded) holds the values and why that is the default |
| `ASPNETCORE_URLS` / `ASPNETCORE_HTTP_PORTS` / `ASPNETCORE_HTTPS_PORTS` | Nothing — [each surface states where it is served](configuration-endpoints.md#where-each-surface-is-served), and setting one of these fails startup with a message naming the key that replaces it |
| `DOTNET_ENVIRONMENT` / `ASPNETCORE_ENVIRONMENT` | The environment name; `Development` is what admits user secrets and `appsettings.Development.json` |
| `DOTNET_USE_POLLING_FILE_WATCHER` | Set to `1` where reload must observe a mounted volume's atomic update — Kubernetes ConfigMaps in particular |
| `OPENSSL_CONF` | The OpenSSL configuration file every TLS connection in the process is handshaked under. Unset is the platform's own policy; setting it is how a mail server the platform refuses is reached at all, and the host warns at startup that it is in force. [The platform TLS policy](platform-tls-policy.md) is the page |

Each of these has a reader that runs before MailFathom's configuration exists, or that never consults it: the bootstrap
logging pipeline is composed before the configuration providers are, because a malformed `appsettings.json` is one of
the failures it exists to report; the OpenTelemetry exporter reads its own `OTEL_*` variables directly; the .NET host
settles the environment name before the application's configuration is composed; and OpenSSL reads `OPENSSL_CONF` while
it initializes, which is the one entry here that could not be a MailFathom setting even in principle.

### Writing one anywhere else fails startup

A value for any of them that did not come from the process environment is refused, naming every such variable at once:

```
Settings only the process environment can deliver carry a value that did not come from it: OPENSSL_CONF,
OTEL_SERVICE_NAME. Each is read before MailFathom's configuration exists, or by a library that never consults it, so a
value written into an appsettings file, a provisioned configuration file, the persisted configuration document, or a
command-line argument reaches nobody. Set each as an environment variable on the host process, or remove it.
```

That failure carries error code `12002` and ends the process through the same bootstrap pipeline every other startup
failure does. It exists because the mistake is otherwise invisible: the configuration pipeline accepts
`"OTEL_SERVICE_NAME"` in a mounted ConfigMap and reads it back happily, while the exporter — which took its value from
the environment long before that file was layered in — keeps reporting under the assembly name. Nothing in the file, in
the logs, or in the process would say which of the two an operator was looking at.

The check compares against the environment rather than merely looking for the name, because the environment provider
puts these names into configuration too, and a value that arrived that way is exactly what a correct deployment looks
like. What it catches beyond an absent variable is an override: a command-line argument outranks the environment
provider, so `--OTEL_SERVICE_NAME=…` leaves configuration reporting one identity while the exporter keeps using another.

Whole families are covered rather than the names in the table alone. Every `OTEL_*`, `ASPNETCORE_*`, and `DOTNET_*`
variable belongs to a reader that takes it from the environment, so naming only the handful MailFathom itself reads
would leave the rest — `OTEL_EXPORTER_OTLP_HEADERS` above all, which carries a collector's credential — silently
ignorable. A blank value counts as unset on both sides, because templating a manifest routinely emits an empty string
for a setting nobody chose.

The three URL-shaped listener addresses are the exception, and they are stricter rather than looser: they are refused
from *every* source, the environment included, because no MailFathom surface is served from one at all.
