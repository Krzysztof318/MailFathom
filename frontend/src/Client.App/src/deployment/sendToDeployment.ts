// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { longestResponseBody, type MailFathomTransport } from '@mailfathom/client-backend';

// The adapter `Client.Backend` asks its caller for. That package declares no DOM, so the one call to `fetch` in the
// client is here — which is what makes the boundary a resolution error rather than a convention, and it is the whole
// of what this module is.

/**
 * The transport for one attempt, which the signal abandons when the person waiting on it gives up.
 *
 * A transport is bound to a signal rather than handed one per request because abandoning is a property of the attempt
 * a screen started, not of a message inside it — and because `AbortSignal` is a browser API, which
 * `MailFathomTransport` may not name for the reason this module exists at all.
 */
export type DeploymentTransport = (abandoned: AbortSignal) => MailFathomTransport;

/** Puts one request on the wire, and reports what came back without deciding anything about it. */
export const sendToDeployment: DeploymentTransport = (abandoned) => async (request) => {
    const response = await fetch(request.path, {
        method: request.method,
        headers: { ...request.headers },
        signal: abandoned,
    });

    return {
        status: response.status,
        body: await readBoundedBody(response),

        // Lower-cased already, which is what `ClientResponse` states its names are: the platform's own header
        // collection normalizes them, so a lookup there needs no second spelling to try.
        headers: Object.fromEntries(response.headers),
    };
};

/**
 * Reads the answer up to the bound the wire states, and stops there.
 *
 * `response.text()` would buffer whatever arrives before anything got to look at it, which is the wrong order at the
 * one boundary in this client where the other side is a stranger: an address is asked whether MailFathom answers there
 * precisely because nobody knows yet. So the body is read in chunks against a running total and the read is cancelled
 * the moment it passes the bound, which also frees the connection rather than draining a stream nothing will use.
 *
 * An answer that goes over comes back empty, which every operation already refuses as unreadable — see
 * `longestResponseBody` for why that is the accurate outcome rather than a lost distinction.
 */
async function readBoundedBody(response: Response): Promise<string> {
    const reading = response.body?.getReader();

    if (reading === undefined) {
        return '';
    }

    // Streaming rather than per-chunk, because a chunk boundary falls wherever the network put it and a multi-byte
    // character split across two of them would otherwise decode as replacement characters on both sides.
    const decoder = new TextDecoder();
    let body = '';
    let read = 0;

    for (;;) {
        const { done, value } = await reading.read();

        if (done) {
            return body + decoder.decode();
        }

        read += value.byteLength;

        if (read > longestResponseBody) {
            await reading.cancel();

            return '';
        }

        body += decoder.decode(value, { stream: true });
    }
}
