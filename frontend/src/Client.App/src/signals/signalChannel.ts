// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { HttpTransportType, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { signalMethod, type MailFathomSignalChannel, type SignalStreamSchedule } from '@mailfathom/client-backend';

// The socket `Client.Backend` asks its caller for. That package declares no DOM, so the SignalR client is named in this
// directory and nowhere else — the same boundary `deployment/sendToDeployment.ts` holds for `fetch`, and for the same
// reason: what a connection *is* belongs to the application, and when it opens, what it presents, what a payload has
// to be, and how long it waits before opening again belong to the package.
//
// Nothing here reads a payload. Whatever the deployment sent is handed over as it arrived, because the one place a
// value from the wire is checked is the package that publishes the type it becomes.

/**
 * Opens one connection to the deployment's signal hub.
 *
 * **WebSockets alone, and no negotiation.** SignalR would otherwise open with an HTTP request that chooses a transport,
 * which is a second request carrying the same ticket — and a ticket opens exactly one connection, so the negotiation
 * would spend it and the socket that followed would be refused. Skipping it is permitted precisely because one
 * transport is named, and a deployment behind a proxy that will not pass the upgrade therefore fails to connect rather
 * than falling back to long polling: the client reads on its own interval, which is what it does anyway.
 *
 * Logging is off because the deployment already records what it refused, and a client that logged its own connection
 * attempts would write the address it presented a ticket to into a browser console.
 */
export const openSignalChannel: MailFathomSignalChannel = async (opening) => {
    const connection = new HubConnectionBuilder()
        .withUrl(opening.url, { transport: HttpTransportType.WebSockets, skipNegotiation: true })
        .configureLogging(LogLevel.None)
        .build();

    connection.on(signalMethod, (payload: unknown) => {
        opening.arrived(payload);
    });

    connection.onclose(() => {
        opening.dropped();
    });

    await connection.start();

    return { close: () => connection.stop() };
};

/** How the stream waits, which is the browser's own timer and the browser's own source of a spread. */
export const browserSchedule: SignalStreamSchedule = {
    wait: (milliseconds) =>
        new Promise((resolve) => {
            window.setTimeout(resolve, milliseconds);
        }),
    draw: () => Math.random(),
};
