// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef } from 'react';
import { useTelemetry } from './clientTelemetry';

// What a person waits for when they move between spaces. The client renders every later screen rather than fetching
// one, so the wait is React's rather than the network's — which is exactly the quantity nobody can see from a
// deployment's own telemetry, and the reason it is measured here at all.
//
// It is measured from the address changing, because that is the moment somebody asked, and it ends after the space
// they asked for has been committed to the document. The address is read through a listener of this hook's own rather
// than by reaching into `routing/useSpace.ts`: what that module answers is which space to render, and the moment the
// question was put is not part of it.

/**
 * Reports how long moving to a space took, once per move.
 *
 * @param space The space now being rendered, or `null` while the deployment has not said which spaces there are.
 */
export function useNavigationTelemetry(space: string | null): void {
    const telemetry = useTelemetry();
    const askedAt = useRef<number | null>(null);

    useEffect(() => {
        function asking(): void {
            askedAt.current = performance.timeOrigin + performance.now();
        }

        window.addEventListener('hashchange', asking);

        return () => {
            window.removeEventListener('hashchange', asking);
        };
    }, []);

    // Nothing is reported for the space a run opens on: nobody moved to it, and timing it would measure the cold start
    // that `mailfathom.client.arrival.duration` already answers. So a move is only ever a stamp this hook took itself.
    useEffect(() => {
        const asked = askedAt.current;

        if (space === null || asked === null) {
            return;
        }

        askedAt.current = null;
        telemetry.navigated(space, asked);
    }, [space, telemetry]);
}
