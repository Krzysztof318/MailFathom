// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

/** One request this package asks its caller to put on the wire. */
export interface ClientRequest {
    readonly method: 'GET' | 'POST' | 'PUT' | 'DELETE';
    readonly path: string;
    readonly headers: Readonly<Record<string, string>>;

    /**
     * What the request states, for a method that carries one, and nothing for a method that does not.
     *
     * It is already a finished string rather than a value to be serialized, because what a route accepts is that
     * route's contract: the operation composing it is the only thing here that knows the shape, and a transport that
     * serialized on its behalf would be a second opinion about the wire in the one module that must have none.
     */
    readonly body?: string;

    /**
     * The most of this one answer the transport reads, in bytes, where the operation knows better than the backstop.
     *
     * `longestResponseBody` is written for an address nobody has trusted yet, and every operation that asks a
     * deployment for something it already composes to a stated size is entitled to say so — a bound that cuts off an
     * answer the service will legitimately send is a defect rather than a protection, and the reader meets it as
     * `unreadable`, which tells them to report a defect for a message that was fine.
     */
    readonly longestAnswer?: number;
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
 * The most of one answer a transport reads before it gives up on it, in bytes.
 *
 * It is the backstop rather than the bound an operation works to: each of those is far tighter and
 * belongs beside the thing it describes, and this is what keeps a body from being buffered whole
 * before any of them can apply. It matters most where the client is asking an address it has not
 * trusted yet whether MailFathom is what answers there — the answer at that point is from a stranger,
 * and a stranger that replies with a gigabyte should cost this client a cancelled read rather than
 * the memory.
 *
 * A transport that has to stop reading answers with an empty body, which every operation already
 * refuses as unreadable. That is the accurate outcome and it needs no reason of its own: an answer
 * this client would not read in full is one it cannot act on either way.
 */
export const longestResponseBody = 1_048_576;

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
