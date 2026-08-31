// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

/** One request this package asks its caller to put on the wire. */
export interface ClientRequest {
    readonly method: 'GET';
    readonly path: string;
    readonly headers: Readonly<Record<string, string>>;
}

/** What came back, reduced to the two things this package reads. */
export interface ClientResponse {
    readonly status: number;
    readonly body: string;
}

/**
 * How a request reaches the service.
 *
 * This package names no HTTP API of its own because it declares no DOM: `fetch` is not in its `lib`, so the adapter
 * that calls one lives in the application and arrives here as this function. The boundary is therefore the reason the
 * indirection exists rather than a layer added in case a second transport ever appears.
 */
export type MailFathomTransport = (request: ClientRequest) => Promise<ClientResponse>;
