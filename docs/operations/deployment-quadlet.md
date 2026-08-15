# Deploying with Podman Quadlet

<!-- describes: deploy/quadlet/** -->

[`deploy/quadlet/`](https://github.com/Krzysztof318/MailFathom/tree/main/deploy/quadlet) runs MailFathom and PostgreSQL
as **rootless systemd units** under Podman. It exists for one reason: a Quadlet `.container` file *is* a systemd unit
source, so a container started from one takes credentials the way the native installation does — `LoadCredentialEncrypted=`,
material that is ciphertext at rest, and a `systemd-credential:` reference that resolves inside the container with no
code change.

**This is an alternative to [Docker Compose](deployment-compose.md), not a replacement for it.**
`deploy/compose/compose.yaml` is unchanged, still runs under `podman compose`, and stays the recommended first
installation. Nothing here is required to keep that deployment working, and the two are not run side by side on one
machine: they publish the same two ports and would contend for them.

## When to choose it, and when not

Choose the Quadlet when **the credentials matter more than the convenience**: a host where secret material must not sit
on disk in plaintext, where the deployment already lives in systemd beside other units, or where the core-dump and
resource bounds want to be unit properties rather than container flags.

Stay on Compose when you want one command to bring a stack up, when the host has Docker rather than Podman, when you
are installing MailFathom for the first time, or when the host's systemd is older than 258 and encryption at rest was
the reason you were reading this page at all. The Compose deployment does everything this one does except the
credential path and the unit-level bounds, and it does it with less to arrange.

| | Compose | Quadlet |
| --- | --- | --- |
| Secret material at rest | A plaintext file the host permissions protect | Ciphertext bound to this machine, decrypted at unit start into memory the kernel keeps out of swap where it can |
| Who may read a secret | Any process that can traverse the mount | The unit's own user, from a directory systemd creates and removes with the unit |
| Core-dump and resource bounds | `deploy.resources`, and a `--ulimit` you pass | `LimitCORE=`, `MemoryMax=`, `CPUQuota=` on the unit, plus `Ulimit=` inside it |
| Ordering and readiness | `depends_on: service_healthy`, within one `up` | `Requires=`/`After=` against a unit that is not *started* until its health check passes |
| Restart, logs, status | `restart:`, `docker compose logs` | `Restart=`, `journalctl --user -u`, `systemctl --user status` |
| File modes on what is mounted | Load-bearing, and a wrong `umask` stops the deployment starting | Not a concern: the container's account **is** you |
| Engines | Docker and Podman | Podman only |
| Rootless or root | Either | Rootless only — `UserNS=keep-id` is not supported for a root container |
| Building from a checkout | `docker compose up --build` | No. It runs a published release image |
| Nightly builds | `compose.nightly.yaml` | No overlay; edit `Image=` deliberately |
| Bringing it up | One command | Five unit files, `systemctl --user` per service |

## What it needs

- **Podman 5.0 or later.** Quadlet itself arrived in 4.4, but two keys these units depend on did not: `Notify=healthy`,
  which is what makes the database's readiness a real ordering rather than an approximation of one, and `Ulimit=`, which
  is the core-dump bound inside the container. Both landed in 5.0. `podman --version`.
- **systemd 258 or later**, for the credential path. A per-user service manager could not decrypt a user-scoped
  credential before 258; an older one fails the unit at its credentials step with
  `Failed to determine local credential key: Permission denied` before Podman is ever executed.
  [What an older systemd leaves you](#what-an-older-systemd-leaves-you) is the fallback. `systemctl --version`.
- **A rootless user with subordinate id ranges**, which every distribution allocates on user creation — `/etc/subuid`
  and `/etc/subgid` must both carry a line for the account. `UserNS=keep-id` consumes the whole of that range.
- **Lingering enabled for that user**, so the per-user service manager runs without a login session:
  `loginctl enable-linger "$USER"`. Without it the deployment stops when you log out.
- **cgroup v2**, so `MemoryMax=` and `CPUQuota=` on a user unit reach the container. Every current distribution
  defaults to it.
- **Ports at 1024 or above.** Rootless Podman publishes nothing below that until `net.ipv4.ip_unprivileged_port_start`
  allows it; the units publish 8080 and 8081, and what meets the limit is a reverse proxy terminating TLS on 443 in
  front of MailFathom rather than MailFathom itself.

## What SELinux costs, and what to do about it

**A container reads a bind-mounted host file only when that file carries the `container_file_t` SELinux label**, and
systemd labels `$CREDENTIALS_DIRECTORY` with its own type instead. On a host where SELinux is enforcing — the Red Hat
family — a container that mounts the directory therefore mounts it successfully and is denied the moment it reads.
systemd has no setting for the label and
[declined to add one](https://github.com/systemd/systemd/issues/36369), on the position that a policy which denies a
service its own credentials is the thing to fix.

The units ask Podman to relabel instead: `Volume=%d:/etc/mailfathom/credentials:ro,Z` gives the credential files a
private container label for the life of the container. That request needs somewhere to write, which is the second
reason these units are *user* units rather than system ones — **the system service manager mounts the credentials
directory read-only**, so the relabel fails there with `lsetxattr … read-only file system` and so does a `chcon` in an
`ExecStartPre=`. On a host where SELinux is not enforcing the `Z` does nothing at all and none of this applies.

If the relabel is refused on your host, two things reach the credential and one of them is not worth taking:

- **`SecurityLabelDisable=true` on the container.** It works, and it turns off SELinux label separation for the
  *entire container* rather than for the one file that needed it — trading confinement of every path the container
  touches for confidentiality of one credential. **MailFathom does not ship it and does not recommend it.** If you set
  it anyway, set it on `mailfathom.container` alone, never on the database, and write down why.
- **Stay on `file:` references for that host**, which is what the Compose deployment does: material in a directory you
  permission, mounted read-only. You give up encryption at rest and keep everything else the Quadlet offers —
  the unit-level bounds, the ordering, the journal. That is the recommended answer, and
  [credentials](deployment-compose.md#credentials) is the shape to copy.

## Before the first start

Everything the deployment reads lives under your home directory. Nothing is read from the repository at run time.

```bash
mkdir -p ~/.config/containers/systemd
mkdir -p ~/.config/mailfathom/config ~/.config/mailfathom/postgres-init
mkdir -p ~/.config/credstore.encrypted
chmod 700 ~/.config/credstore.encrypted
```

`~/.config/credstore.encrypted/` is systemd's own location for encrypted credentials, which is why it is used here
rather than a directory of MailFathom's. The `0700` is ordinary hygiene rather than the protection: what protects the
material is that each file is ciphertext.

### The unit sources

```bash
cp deploy/quadlet/mailfathom.container deploy/quadlet/mailfathom-postgres.container \
   deploy/quadlet/*.network deploy/quadlet/*.volume ~/.config/containers/systemd/
cp deploy/quadlet/config/10-mailfathom.json.example ~/.config/mailfathom/config/10-mailfathom.json
cp deploy/compose/postgres/10-create-mailfathom-database.sh ~/.config/mailfathom/postgres-init/
```

The two `.container` files are named rather than globbed because there are two more, `mailfathom-presidio.container` and
`mailfathom-spamassassin.container`, and those are the units here that are optional: copying one is half of switching
its feature on, and a deployment that wants neither never copies either. See [Personal-data
scanning](#personal-data-scanning) and [Spam scanning](#spam-scanning) below.

The last line is not a mistake. The database initialization script is the Compose deployment's, reused rather than
forked, which is why the database container mounts its credentials at `/run/secrets`: that is the path the script
already reads. One file describes how the role, the database, and the `vector` extension are created, and both shapes
meet it. Keep its executable bit — `cp` preserves it under an ordinary `umask` — because the image's entrypoint
*executes* a script in that directory that carries the bit and *sources* one that does not.

**Both units assert those two files rather than the directories holding them**, and it is worth knowing why before a
start fails naming one. Podman creates a missing bind source as an empty directory instead of refusing, and the `mkdir`
above creates both directories a step earlier, so a skipped or mistyped `cp` leaves something that exists and is empty.
An empty `postgres-init` initializes a database with no `mailfathom` role and then reports healthy, and an empty
`config` starts MailFathom with synchronization off and no account. Neither says why, which is what the assertions are
for: `AssertFileIsExecutable=` on the initialization script, and `AssertPathExistsGlob=` on `*.json` in the
configuration directory — the latter also catching the directory that holds only the tracked `.json.example`, which
MailFathom does not read.

Then edit `~/.config/containers/systemd/mailfathom.container` and replace `<version>` in `Image=` with the release you
are installing. The placeholder is invalid on purpose, so an unedited unit fails with an unparseable image reference
rather than pulling whatever a moving tag points at today.

### The credentials

Each one is encrypted once, under the name the configuration references. **That name appears in three places and is one
decision**: the `--name=` below, the credential id on the `LoadCredentialEncrypted=` line in the unit, and the
`systemd-credential:` reference in the configuration. systemd authenticates the name, so a mismatch fails the unit with
`Name in credential doesn't match expectations` rather than handing over the wrong material.

```bash
openssl rand -base64 33 | tr -d '\n' \
  | systemd-creds --user encrypt --name=postgres-superuser-password - \
      ~/.config/credstore.encrypted/postgres-superuser-password

openssl rand -base64 33 | tr -d '\n' \
  | systemd-creds --user encrypt --name=mailfathom-database-password - \
      ~/.config/credstore.encrypted/mailfathom-database-password

openssl rand -base64 33 | tr -d '\n' \
  | systemd-creds --user encrypt --name=mcp-workstation-key - \
      ~/.config/credstore.encrypted/mcp-workstation-key

systemd-ask-password -n \
  | systemd-creds --user encrypt --name=imap-primary-password - \
      ~/.config/credstore.encrypted/imap-primary-password
```

The mailbox password arrives through `systemd-ask-password -n` so that it is neither a shell-history entry nor a file
you have to remember to delete. `-n` is what keeps a trailing newline out of the material.

**Each unit reaches only the credentials it lists**, which is what the two `LoadCredentialEncrypted=` lines in
`mailfathom-postgres.container` and the three in `mailfathom.container` are: a grant rather than a manifest. The
database superuser password is on the first list and not the second, so it is never on a path MailFathom can read —
the same property the Compose deployment gets by keeping those two credentials out of the mounted secrets directory.

`--user` is required and is not a convenience: a system-scoped credential cannot be decrypted by a per-user service
manager at all. It also narrows the key — the user's uid, user name, and the machine id are folded into it — so a
credential encrypted for one account does not open for another on the same host. `--with-key=null` is refused outright
in this mode (*selected key not available in `--uid=` scoped mode, refusing*), so a user-scoped credential is always
bound to the machine.

The command reaches the key through a system service on `/run/systemd/io.systemd.Credentials`, which is what lets an
unprivileged user encrypt against material it cannot read: the host key lives in `/var/lib/systemd/credential.secret`,
is root-only, and is generated by that service the first time anything asks for one.
[Secret provisioning](secret-provisioning.md#what-an-encrypted-credential-is-bound-to) states what `--with-key=auto`
selects and what each choice binds the file to.

**A mailbox that authenticates with OAuth needs one credential more**: the data-encryption key its refresh token is
sealed under. That one is generated to a length rather than to a strength, and startup refuses any other:

```bash
openssl rand -base64 32 \
  | systemd-creds --user encrypt --name=mailfathom-data-key - \
      ~/.config/credstore.encrypted/mailfathom-data-key
```

That is `-base64 32`, never the `-base64 33` above it — the material has to decode to exactly 32 bytes, which is what
AES-256 takes. Uncomment the matching `LoadCredentialEncrypted=` line in `mailfathom.container` and the
`DataEncryption` block in the configuration together with it. **Back up the base64 the command printed, with the
database, and never the `.cred` file instead**: the sealed file opens on this machine and on no other, so a database
restored beside a copy carried off a host that no longer exists restores nothing that was sealed.
[The data-encryption key](secret-provisioning.md#the-data-encryption-key) covers rotation and what the ring is for, and
[what an encrypted credential is bound to](secret-provisioning.md#what-an-encrypted-credential-is-bound-to) states the
binding in full — including that a firmware update does not invalidate the credential, and which flag makes the TPM2
chip a requirement rather than a preference.

Adding an account later is two lines rather than one: the credential, and a `LoadCredentialEncrypted=` line in
`mailfathom.container` naming it. That the grant is explicit is the point — the unit reaches exactly the credentials it
lists and nothing else in the store.

### The configuration

`~/.config/mailfathom/config/10-mailfathom.json` is ordinary configuration, layered under the unit's environment block.
Edit the mailbox, and read [configuration sources](configuration-sources.md) for the precedence and
[the configuration reference](configuration-reference.md) for every key. The database is configured by the unit rather
than by this file, so it is deliberately absent from the example.

## Starting

```bash
loginctl enable-linger "$USER"
systemctl --user daemon-reload                       # regenerates the units from the Quadlet sources
systemctl --user start mailfathom-postgres.service   # creates the role, the database, and the vector extension
# apply the schema — see below
systemctl --user start mailfathom.service
```

`systemctl --user daemon-reload` is what turns the `.container`, `.network`, and `.volume` files into services, and it
is needed after **every** edit to one of them. Quadlet units are generated, so `systemctl --user enable` does not apply
to them; the `[Install] WantedBy=default.target` in each file is what starts them at boot once lingering is on.

The middle step is separate on purpose, and nothing in this deployment performs it. MailFathom verifies the schema and
refuses to serve against one it does not recognize, so starting the service after a version change *tells* you a
migration is outstanding rather than silently applying one:

```
MailFathom.Application.Persistence.DatabaseSchemaOutOfDateException: The database has not applied 1 migration(s) this
build defines: 20260731132336_Initial.
```

The step is `mailfathom-schema-<version>.sql`, attached to the release you are installing. The database publishes no
port and both credentials are already inside its container, so the shortest route is to run `psql` there and hand it the
script on standard input, which also keeps the credential off a command line:

```bash
podman exec --interactive mailfathom-postgres sh -c \
  'PGPASSWORD="$(cat /run/secrets/mailfathom-database-password)" exec psql \
     --username "$MAILFATHOM_DATABASE_ROLE" --dbname "$MAILFATHOM_DATABASE" --set ON_ERROR_STOP=on' \
  < 'mailfathom-schema-<version>.sql'
```

**As `mailfathom`, never as `postgres`.** Read the SQL before applying it and take a backup first;
[applying the database schema](database-schema.md) states the privileges it needs, the locks it takes, why the role
that runs it becomes the owner of everything it creates, and what each startup failure means.

## Checking it

```bash
systemctl --user status mailfathom-postgres.service mailfathom.service
curl -fsS http://127.0.0.1:8081/started              # has it finished coming up
curl -fsS http://127.0.0.1:8081/health               # readiness, including the database
curl -fsS http://127.0.0.1:8081/alive                # liveness, the process alone
journalctl --user -u mailfathom.service -f
```

The probes answer on **8081**, not on the port the MCP endpoint is served on, and they carry no credential — so which
address their port is published on is what controls who may ask them. [The health endpoints](health-endpoints.md)
states what each one consults and how to move or turn off the listener.

The MailFathom container declares no health check: its image carries no shell and no HTTP client for one to run in, so
the endpoints above are asked from outside. The database container does declare one, and its unit is not considered
started until it passes — which is what makes `Requires=mailfathom-postgres.service` on the MailFathom unit an ordering
against a database that answers rather than against a container that exists.

The MCP endpoint answers at `/mcp` and is off until the configuration enables it. Read
[the MCP endpoint](mcp-endpoint.md) before you do; an enabled endpoint must state how it is authenticated, and there is
no default.

## What an older systemd leaves you

Below systemd 258 the encrypted path is unavailable and the unit fails before Podman runs. Everything else in this
deployment still works, and `LoadCredential=` is what replaces the encrypted line. The material goes in
`~/.config/credstore/` — systemd's plain-credential location, the one beside the encrypted store used above — and each
line names it by absolute path for the same reason the encrypted ones do, so a missing file fails the unit:

```bash
mkdir -p ~/.config/credstore
chmod 700 ~/.config/credstore
systemd-ask-password -n > ~/.config/credstore/imap-primary-password
chmod 400 ~/.config/credstore/imap-primary-password
```

```ini
[Service]
LoadCredential=imap-primary-password:%h/.config/credstore/imap-primary-password
```

What you keep is most of what the shape was for: the material is copied into a per-unit directory readable by your
account alone, kept out of swap where the platform permits, and removed with the unit — and every
`systemd-credential:` reference resolves unchanged. What you give up is encryption at rest, so the source file is a
plaintext file the host's permissions protect, exactly as under Compose. The `0700` and the `0400` above are that
protection rather than hygiene here, which is the difference from the encrypted store; keep the file out of any backup
that the database's own backup does not already cover.

## Upgrading

Back up the database, apply the new release's schema artifact, then move the image. That order is the one with no
window in which nothing serves: the new image refuses to start against a schema behind it, and the running one keeps
serving against a schema ahead of it.

```bash
podman exec --interactive mailfathom-postgres sh -c \
  'PGPASSWORD="$(cat /run/secrets/mailfathom-database-password)" exec psql \
     --username "$MAILFATHOM_DATABASE_ROLE" --dbname "$MAILFATHOM_DATABASE" --set ON_ERROR_STOP=on' \
  < 'mailfathom-schema-<version>.sql'                      # the version being upgraded to

$EDITOR ~/.config/containers/systemd/mailfathom.container   # Image=, to the new version
systemctl --user daemon-reload
systemctl --user restart mailfathom.service
```

Rolling back is the same sequence with the previous image. **A schema change is not rolled back by it**;
[rolling back](database-schema.md#rolling-back) states when restoring the database is necessary and when rolling only
the image back is enough.

Nothing here moves an image on its own. `AutoUpdate=` is deliberately absent from both units, because an upgrade has a
schema step in front of it and is a decision rather than a background task.

**Upgrading a deployment whose volume was written by PostgreSQL 17** is the same dump-and-restore this deployment's
database image needs anywhere, and
[the Compose page describes it in full](deployment-compose.md#upgrading-a-deployment-that-ran-postgresql-17) — including
the two flags that keep the restore clean, which are what separates a failed migration from one that merely reports an
error after the rows are already in. Read the sequence there and substitute the commands: `systemctl --user stop
mailfathom.service mailfathom-postgres.service` for `docker compose down`, `podman run`, `podman exec`, and
`podman volume rm` for their Docker equivalents, and `systemctl --user start mailfathom-postgres.service` for
`docker compose up -d postgres`. The container is `mailfathom-postgres` and the volume is `mailfathom-postgres-data`.

## Backup, and what survives removal

The synchronized mail lives in `mailfathom-postgres-data`, a named volume nothing in this deployment removes.

```bash
podman exec mailfathom-postgres \
  pg_dump --username mailfathom --format custom mailfathom > mailfathom-$(date +%F).dump

podman exec --interactive mailfathom-postgres \
  pg_restore --username mailfathom --dbname mailfathom --clean --if-exists < mailfathom-2026-08-12.dump
```

Neither passes a password, because both reach the server over its Unix socket from inside the container, where the
image's own `initdb` left local connections trusted.

Stopping the units leaves the volume. Removing it is `podman volume rm mailfathom-postgres-data`, an explicit act,
because rebuilding it costs a full IMAP resynchronization — and everything that is not in the mailbox, the answering
audit trail and the embeddings, is regenerated rather than refetched.

Stopping them can also report `rootless netns: kill network process: permission denied`, which is the host's AppArmor
policy refusing a signal rather than a unit that failed to stop. These containers are torn down through the same
rootless network namespace as the Compose deployment's and end on the same step, so
[the error a rootless-Podman teardown reports](deployment-compose.md#the-error-a-rootless-podman-teardown-reports)
holds here unchanged.

## Uninstalling

```bash
# mailfathom-presidio.service only where the analyzer unit was installed; stopping MailFathom does not stop it, because
# the ordering runs the other way.
systemctl --user stop mailfathom.service mailfathom-presidio.service mailfathom-postgres.service
rm ~/.config/containers/systemd/mailfathom*.{container,network,volume}
systemctl --user daemon-reload

podman volume rm mailfathom-postgres-data      # destroys the mail
rm -r ~/.config/mailfathom ~/.config/credstore.encrypted
loginctl disable-linger "$USER"                # only if nothing else of yours runs as a user service
```

## Personal-data scanning

Quadlet has no equivalent of Compose's profiles, so how this feature is switched off is that its unit is not installed —
and off is the default. A deployment that wants secrets only copies the files above and never
`mailfathom-presidio.container`, and then no image is pulled, no container exists, and none of its two gigabytes is held.
[The personal-data scanner](../features/sensitive-content-scanning.md#the-personal-data-scanner) records what the feature
hides and what each category costs retrieval.

Switching it on is three edits, and each half alone is a deployment that does not work:

```bash
cp deploy/quadlet/mailfathom-presidio.container ~/.config/containers/systemd/
```

Then in `~/.config/containers/systemd/mailfathom.container`, uncomment the two ordering lines in `[Unit]` and the four
`Environment=` lines for `SensitiveContent`, and `systemctl --user daemon-reload`. The analyzer unit declares
`Notify=healthy` exactly as the database unit does, so that ordering waits for an analyzer that *answers* rather than a
container that exists. Nothing breaks without it — MailFathom starts either way and reports itself unready until the
analyzer answers — but the analyzer loads a language model before it serves anything, and ordering the two is what keeps
that interval out of the application's log and off its readiness probe. `TimeoutStartSec=300` is what allows for that
load. Nothing else in the start sequence changes: the uncommented `Requires=` is what pulls the analyzer in when
`mailfathom.service` starts, and `systemctl --user status mailfathom-presidio.service` is where its own health check is
read while the model loads.

To use an analyzer you already operate, copy no unit and point the endpoint line at its address. Keep that address
**inside your own network**: the point of scanning is that content is inspected before it leaves the trust boundary, and
the feature page states what pointing it outside gives up.

The analyzer unit is the shortest file in the directory, and what it does not have is the point of it: no credentials, no
volumes, no configuration, and no `PublishPort=`. It receives request bodies and answers offsets, so it is attached to
`mailfathom-backend.network` alone and reachable from MailFathom and nothing else. It needs no `UserNS=keep-id` either,
because nothing in it reads a file you own.

**The language line is the one that needs the image behind it.** The commented `SensitiveContent__PersonalDataAnalyzer__Language`
line names `en`, and the pinned image is built with an English model and an English recognizer registry. Naming another
code there leaves MailFathom permanently unready rather than scanning in that language: the analyzer answers that it
recognises nothing for it. Running the analyzer in a second language means an image of your own, named on the `Image=`
line of the copied unit — [the analyzer's language](personal-data-analyzer-languages.md) records what that takes and
which identifiers each language reaches.

## Spam scanning

The same shape, and the same default: `mailfathom-spamassassin.container` is a file you either copy or do not, and not
copying it is how the feature is off. [Spam classification](../features/spam-classification.md) records what a
classification holds and what the scanner adds.

```bash
cp deploy/quadlet/mailfathom-spamassassin.container ~/.config/containers/systemd/
```

Then in `~/.config/containers/systemd/mailfathom.container`, uncomment the two ordering lines in `[Unit]` and the four
`Environment=` lines for `SpamClassification`, and `systemctl --user daemon-reload`. The unit declares `Notify=healthy`
like the others, so the ordering waits for a daemon that answers: it compiles its rule corpus before it listens, and
MailFathom refuses to start while nothing answers, which is what `TimeoutStartSec=300` allows for.

To use a daemon you already operate, copy no unit and point `SpamClassification__Scanner__Host` at its address. Keep it
**inside your own network**: the daemon is sent whole messages unredacted, and the feature page states what pointing it
outside gives up.

The unit has no credentials, no volumes, no configuration, and no `PublishPort=`, and it needs no `UserNS=keep-id`
because nothing in it reads a file you own. It is attached to `mailfathom-backend.network` alone, which is the one place
this deployment differs from the Compose one: with no route out, the daemon cannot fetch the rule updates it tries for on
start and daily afterwards, so its corpus stays whatever the image was built with — which scores today's mail worse than
a fresh one. Add `mailfathom-frontend.network` to the unit to give it those updates. Either way `DNS_CHECKS=0` keeps the
blocklist rules that would send sending addresses and URI host names to third parties switched off.

It is also the one unit here granted a capability back. It binds its port as root and runs every scan as an unprivileged
account, which is what parses the mail, so `SETUID` and `SETGID` are added after all capabilities are dropped; without
them the daemon refuses to start.

## Bounds

Every knob is in the unit file, edited in place; there is no `.env` equivalent, which is one of the things this shape
trades away. The values the units apply:

| Where | What |
| --- | --- |
| `[Service] MemoryMax=`, `CPUQuota=` | 1 GiB and two cores per unit, applied by systemd to the whole cgroup rather than to one container inside it. The analyzer's is 2 GiB, because it holds a language model for the life of the container and below roughly one gigabyte is killed while loading; the spam daemon's is 512 MiB, which holds its compiled rule corpus and a child per scan |
| `[Service] LimitCORE=0`, `[Container] Ulimit=core=0` | No core dump from the process holding decrypted material, or from what it starts |
| `[Container] StopTimeout=`, `[Service] TimeoutStopSec=` | 60 and 90 seconds, so the host finishes its shutdown drain rather than being killed mid-run |
| `[Container] Tmpfs=/tmp` | 64 MiB, the one writable path the runtime needs on an otherwise read-only root filesystem |
| `[Container] PublishPort=` | `127.0.0.1:8080` for `/mcp`, `127.0.0.1:8081` for the probes |

Raise `StopTimeout=` and `TimeoutStopSec=` together if a deployment configures a longer
`MailSynchronization:ShutdownDrainTimeout`.

## Related

- [Secret provisioning](secret-provisioning.md) — the reference grammar, every deployment shape's mapping, and what a
  leaked configuration file does and does not expose
- [Deploying with Docker Compose](deployment-compose.md) — the same stack without the credential path, and the pages
  this one deliberately does not repeat: the network boundary, the PostgreSQL 17 upgrade, and what `up` and `down` do
- [Applying the database schema](database-schema.md) — the release artifact, the privileges it needs, and the three
  startup failures it answers
- [The container image](container-image.md) — what is inside it, how it runs, and why it carries no schema tool
- [The platform TLS policy](platform-tls-policy.md) — for a mail server whose handshake the container's own OpenSSL
  refuses; the file is mounted into the container and named in the unit's environment
- [Configuration sources](configuration-sources.md), [the MCP endpoint](mcp-endpoint.md),
  [health endpoints](health-endpoints.md)
