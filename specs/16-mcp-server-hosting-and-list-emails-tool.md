# MCP Server Hosting and the `list_emails` Tool

**Roadmap group:** D — read side and MCP
**Draft delivery stage:** 4
**Depends on:** 13
**Estimated change size:** ~850 lines including tests and documentation

## Goal

Stand up the MCP protocol surface and establish the conventions every later tool follows, delivering `list_emails` as the reference implementation.

## Current state

`src/Mcp` contains a project file referencing `ModelContextProtocol.AspNetCore` 1.4.1 and nothing else. The host exposes a root readiness response and the shared service-defaults endpoints. There is no MCP endpoint, no tool, no error mapping, and no authentication.

## Approved scope

The `Mcp` project gains the tool-descriptor conventions, the request-to-application mapping, and the result and error mapping that draft sections 13.5 and 17 require. The host maps the Streamable HTTP transport endpoint. `Mcp` contains no persistence, no mail protocol, and no transaction logic; it translates and nothing else.

Every tool descriptor is written deliberately with a human-readable title, input and output schemas, a safe description, and the behavior annotations the draft names: `readOnlyHint` true, `destructiveHint` false, `idempotentHint` true, and `openWorldHint` false. These are contract metadata, so the advertised `tools/list` output is asserted by test rather than assumed.

Error mapping is defined once here and reused by every later tool. Expected application failures map to stable machine-readable codes with safe human-readable messages. Nothing crosses the boundary that draft section 13 forbids: no exception type, no stack trace, no inner-exception detail, no provider payload, no internal identifier. An unexpected exception maps to one generic code and is logged with correlation, not returned.

`list_emails` maps the request contract from specification 13, enforces the maximum page size of 100 at the protocol boundary as well as in the use case, and returns summaries with the freshness information the use case provides.

## Interim security posture

OAuth 2.1 and mTLS are stage 9 of the draft and are not in this roadmap segment. The owner has decided that this stage may bind the MCP endpoint on a non-loopback address so real MCP clients can be exercised during development, on the basis that release is still far off. This specification therefore imposes no address restriction.

What it does impose is that the posture is explicit rather than accidental. The endpoint is disabled by default and requires a deliberate opt-in. When it is enabled while transport authentication is absent, startup logs a single unambiguous warning naming the missing controls and the specification that adds them, so nobody discovers months later that a mailbox was reachable without a token. The documented consequence, stated in the operations page rather than enforced in code, is that an endpoint enabled before stage 9 is unauthenticated and should be pointed at development mailboxes only.

Authorization checks are written here rather than deferred, per the rule that authorization lives close to the use case: the tool handler resolves the configured owner and the requested accounts and rejects anything outside them. Stage 9 then adds transport identity to an existing authorization decision instead of introducing authorization for the first time.

## Safety and privacy

Tool inputs are untrusted and are validated at the protocol boundary before reaching the application, with invariants re-enforced in the use case. Output is bounded by the page-size limit. Logs record the tool name, outcome, and duration, never filter values, mailbox addresses, subjects, or result content, since a filter argument is itself sensitive.

## Testing

`Mcp.UnitTests` cover: advertised `tools/list` metadata including every annotation, request mapping for each filter, page-size enforcement at the boundary, mapping of each expected application failure to its stable code, an unexpected exception producing the generic code with no leaked detail, and rejection of an account the owner does not control. A host-level unit test asserts that the endpoint is off by default and that enabling it without transport authentication emits the warning.

## Out of scope

`get_email_content` and `search_emails`, which specifications 17 and 18 own. `ask_mail` and all RAG work. OAuth, mTLS, rate limiting, and CORS.

## Definition of done

- An MCP client can call `list_emails` over Streamable HTTP and receive bounded, paginated summaries.
- Advertised tool metadata matches the draft's annotation requirements, proven by test.
- No error response contains an exception type, stack trace, or internal identifier.
- The endpoint is off by default, and enabling it without transport authentication warns explicitly at startup.
- `docs/features/` documents the tool contract, annotation conventions, and error codes; `docs/operations/` records the interim unauthenticated posture and its expiry at stage 9.
- `dotnet msbuild eng/CodeCoverage.proj -t:Collect` passes the 85% whole-scope gate.
