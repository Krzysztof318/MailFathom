// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailFathomTransport } from '@mailfathom/client-backend';

// The adapter `Client.Backend` asks its caller for. That package declares no DOM, so the one call to `fetch` in the
// client is here — which is what makes the boundary a resolution error rather than a convention, and it is the whole
// of what this module is.

/** Puts one request on the wire, and reports what came back without deciding anything about it. */
export const sendToDeployment: MailFathomTransport = async (request) => {
    const response = await fetch(request.path, { method: request.method, headers: { ...request.headers } });

    return {
        status: response.status,
        body: await response.text(),

        // Lower-cased already, which is what `ClientResponse` states its names are: the platform's own header
        // collection normalizes them, so a lookup there needs no second spelling to try.
        headers: Object.fromEntries(response.headers),
    };
};
