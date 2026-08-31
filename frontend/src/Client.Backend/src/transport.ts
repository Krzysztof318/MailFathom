// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

/** One request this package asks its caller to put on the wire. */
export interface ClientRequest {
    readonly method: 'GET';
    readonly path: string;
    readonly headers: Readonly<Record<string, string>>;
}

/** What came back, reduced to the three things this package reads. */
export interface ClientResponse {
    readonly status: number;
    readonly body: string;

    /**
     * The response headers, under lower-case names.
     *
     * A refusal is the one answer this package reads a header off: it carries the protection space the deployment
     * challenges for, which is what tells a client it reached MailFathom rather than something else refusing it. The
     * names arrive lower-cased because HTTP field names are case-insensitive, and a lookup that had to try three
     * spellings would be a lookup that misses the fourth.
     */
    readonly headers: Readonly<Record<string, string>>;
}

/**
 * How a request reaches the service.
 *
 * This package names no HTTP API of its own because it declares no DOM: `fetch` is not in its `lib`, so the adapter
 * that calls one lives in the application and arrives here as this function. The boundary is therefore the reason the
 * indirection exists rather than a layer added in case a second transport ever appears.
 */
export type MailFathomTransport = (request: ClientRequest) => Promise<ClientResponse>;

/**
 * Puts one request on the wire, answering `null` where nothing answered at all.
 *
 * Every operation goes through this rather than calling the transport directly, because a connection refused, a name
 * that does not resolve, a certificate the client will not accept, and an answer cut short all arrive as a rejected
 * promise rather than as a status — and an operation that let one through would hand a screen an exception where its
 * whole contract is that an expected failure is a value.
 */
export async function send(transport: MailFathomTransport, request: ClientRequest): Promise<ClientResponse | null> {
    try {
        return await transport(request);
    } catch {
        return null;
    }
}
