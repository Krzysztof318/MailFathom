# Architectural Decision Records (ADRs)

An Architectural Decision is a justified software design choice that addresses an architecturally significant functional or non-functional requirement. An Architectural Decision Record captures one decision and the rationale for it.

For background on ADRs, see <https://adr.github.io/>.

## How MailMcp uses ADRs

1. Create or modify an ADR only after explicit owner approval for that documentation change.
2. Copy `docs/decisions/adr-template.md` to `docs/decisions/NNNN-title-with-dashes.md`, where `NNNN` is the next sequence number.
   1. Check existing branches and pull requests when possible so the sequence number does not collide.
   2. Use `docs/decisions/adr-short-template.md` only for small decisions whose trade-offs are already clear.
3. Edit the new ADR.
   1. Initial status is normally `proposed`.
   2. `deciders` lists the people who approve the decision.
   3. `consulted` lists people whose input was sought.
   4. `informed` lists people who must know about the decision but do not approve it.
4. For each option, record meaningful good, neutral, and bad consequences.
5. Update the status to `accepted` only after the decision is approved.
6. Supersede old decisions with a new ADR instead of silently rewriting historical rationale.

## Records

- [0001: Use application-owned repository ports for persistence access and keep EF Core behind infrastructure adapters](0001-application-owned-repositories-for-persistence-ports.md)
- [0002: Use an application-owned configuration access layer for reading, mapping, and reloadable business settings](0002-configuration-reading-mapping-and-reload-boundary.md)
- [0003: Give every first-party failure one base type and a five-digit stable error code](0003-first-party-exception-hierarchy-and-stable-error-codes.md)
