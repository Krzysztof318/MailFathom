// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// How long this client waits before reaching for a deployment that did not answer. It is stated once for the package
// because two things reach: the polling the shell does when a deployment stops answering, and the signal channel
// reopening after it dropped. Two schedules would be two answers to one question, and a fleet of clients coming back
// in step is exactly what the spread below exists to prevent.

/** The most times the client reaches for a deployment that is not answering before it waits to be asked. */
export const mostReconnectionAttempts = 5;

const firstReconnectionDelay = 1_000;
const longestReconnectionDelay = 30_000;

/**
 * How long to wait before the attempt after this one, in milliseconds.
 *
 * The wait doubles and is capped, so a deployment that is down for a while is not asked hundreds of times, and the
 * spread keeps a fleet of clients that all lost the same deployment from coming back in step with each other.
 *
 * @param made How many automatic attempts have already been made since the last answer.
 * @param drawn A value in `[0, 1)`, which the caller draws so that this stays a function of its arguments.
 * @returns The delay to wait, between three quarters and five quarters of the nominal one for that attempt.
 */
export function reconnectionDelay(made: number, drawn: number): number {
    const nominal = Math.min(firstReconnectionDelay * 2 ** made, longestReconnectionDelay);

    return Math.round(nominal * (0.75 + drawn / 2));
}
