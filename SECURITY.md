# Security policy

MailFathom holds mailbox credentials, OAuth tokens, certificate material, and a durable local copy of someone's mail. A vulnerability here reaches personal data directly, so reports are welcome and are treated as the highest-priority work in the repository.

## Reporting a vulnerability

**Do not open a public issue, a discussion, or a pull request for a security vulnerability.**

Report it privately, through either channel:

- **Email <security@mailfathom.org>.** This channel always works and needs no GitHub account. Encrypt if you prefer; ask in a first message carrying no details and a key will be supplied.
- **GitHub private vulnerability reporting** — the *Report a vulnerability* button on the repository's **Security** tab, or [this form](https://github.com/Krzysztof318/MailFathom/security/advisories/new) directly. It needs a GitHub account and opens a draft security advisory, which is where the fix, the credit, and any CVE request are then handled.

A useful report contains the affected version or commit, the configuration the issue needs, reproduction steps or a proof of concept, and what an attacker gains. Say so explicitly if you intend to publish, and when.

**Send no real personal data.** Do not attach a real mailbox, a real message, a real credential, or another person's information; synthetic data reproduces every issue this project can have, and a report is not a lawful place to put someone's mail.

## What to expect

| Stage | Target |
|---|---|
| Acknowledgement that the report arrived | 3 working days |
| Initial assessment, with a severity and a decision | 10 working days |
| Progress updates while a fix is being prepared | every 14 days |
| Coordinated disclosure | within 90 days of the report, or sooner once a fix is available |

MailFathom is maintained by one person, so these are honest targets rather than a service level. If a deadline passes in silence, send a reminder to the same address — silence is an oversight, never a decision to ignore a report.

A confirmed vulnerability is fixed on `main`, published as a GitHub Security Advisory with a CVE where one is warranted, and noted in the release that carries the fix. You will be credited under the name you choose, or not credited if you prefer. There is no bug bounty.

## Supported versions

A confirmed vulnerability is fixed on `main` and reaches you in a release that carries the fix. **Only the newest released minor line is patched by default**, from the permanent `release/<major>.<minor>.x` branch that [`docs/decisions/0004-versioning-and-release-policy.md`](docs/decisions/0004-versioning-and-release-policy.md) describes. Reaching further back is a deliberate decision recorded on the issue that asks for it, never something that follows from an older branch still existing: one maintainer cannot support an unbounded number of lines, and a policy implying otherwise would make a promise this project cannot keep.

| Version | Supported |
|---|---|
| `0.7.x` | Yes — the newest released minor line |
| `0.6.x` | Only by a decision recorded on the issue asking for it |
| Any older release line | Only by a decision recorded on the issue asking for it |
| `main`, and the `-nightly.<n>` builds from it | No. A fix lands here first, but nothing built from `main` carries a release promise |

## Scope

**In scope** — anything in this repository that an operator runs or an artifact carries:

- the MCP endpoint and its authentication, authorization, rate limiting, origin handling, and TLS and mutual-TLS configuration;
- handling of mail content: MIME parsing, HTML sanitization, and anything that can turn an attacker-authored message into code execution, a request, or an escape from the reader's context;
- credential, token, and certificate handling, including anything that puts one in a log, an error, a trace, a tool response, or the database in a form it should not take;
- unauthorized access to stored mail, metadata, embeddings, or retrieval snippets, including a query that returns another account's data;
- SQL injection, request forgery, path traversal, deserialization, and the ordinary web and protocol classes;
- the deployment assets under `deploy/`, the container image, and the Helm chart's defaults, where a default is unsafe rather than merely permissive;
- a workflow under `.github/` that could be made to run untrusted code or leak a token.

**Out of scope:**

- a vulnerability in a third-party dependency with no MailFathom-specific exploitation path — report it upstream, and tell us if MailFathom's use of it makes the impact worse or reachable where upstream says it is not;
- an operator's own misconfiguration, such as an endpoint deliberately run unauthenticated, or a secret placed in a world-readable file;
- anything that presupposes an attacker already holds the host machine, the database, the configuration, or a valid API key;
- missing hardening headers, a weak cipher suite offered but not negotiated, or a scanner's output with no demonstrated impact;
- social engineering, physical access, and denial of service by ordinary resource exhaustion, unless a small unauthenticated request causes disproportionate work.

Ask if you are unsure. A report that turns out to be out of scope costs a reply; an unreported vulnerability costs more.

## Safe harbour

Research conducted in good faith under this policy is authorized, and no legal action will be pursued for it. Good faith means testing against your own installation and your own mailbox, making a genuine effort to avoid privacy violations, data destruction, and service disruption, not accessing or retaining anyone else's data, and giving the maintainer a reasonable chance to fix the issue before disclosing it.

This authorization is what the maintainer can give. It does not waive the rights of a third party, and it does not authorize anything against a mail provider, a hosting provider, or another service MailFathom talks to; those have their own policies and their own boundaries.

## The invariants a report can hold MailFathom to

This section is about the software rather than about reporting. It states the rules the code is written and reviewed against, so a report can name the one that was broken.

- Mail is retrieved **read-only**. Synchronization and content retrieval never set the remote IMAP `\Seen` flag, and every content-fetch path is tested for it. What can write to a mailbox is what the deployment's own configuration turned on — a mail rule action, or a spam classification action — and nothing reachable over the network asks for one.
- Credentials, tokens, and certificate material are read through secret references rather than written into configuration as values. [`docs/operations/secret-provisioning.md`](docs/operations/secret-provisioning.md) describes the provisioning paths.
- Message bodies, attachment content, raw MIME, credentials, and tokens are never written to logs, traces, or error responses.
- Mail content, metadata, and anything derived from them are classified as personal data by default. A derived index inherits the classification of the mail it came from rather than being treated as anonymous.
- MailFathom talks to the mail servers and to the database the operator configured, and to nothing else. No telemetry, no usage reporting, and no call to a service the operator did not name.

An observation that one of these does not hold is a valid report even where the effect looks minor.
