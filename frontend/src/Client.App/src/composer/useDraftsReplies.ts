// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useState } from 'react';
import {
    draftsMailReplies,
    type ClientSession,
    type DeploymentSession,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import { offers } from '../shell/capabilities';

/**
 * Whether the deployment writes a draft for somebody, as it last answered.
 *
 * It is read again every time the deployment's session is — after a deployment that stopped answering answers again,
 * and after every finished synchronization run — because that is the moment this client already re-asks what the
 * deployment offers. A read that failed settles nothing about it, so it is never left as the answer for the rest of a
 * sign-in: what stands is kept while the next read is in flight or fails, and the next re-read of the session asks
 * again.
 *
 * @param session Who is asking, and where, or `null` where nobody is signed in.
 * @param offered What the deployment last said the credential may do, or `null` where it has not said yet.
 * @param transport How the read reaches the deployment.
 * @returns Whether the deployment last answered that it drafts.
 */
export function useDraftsReplies(
    session: ClientSession | null,
    offered: DeploymentSession | null,
    transport: MailFathomTransport,
): boolean {
    const [draftsReplies, setDraftsReplies] = useState(false);
    const asksMail = offered !== null && offers(offered, 'askMail');

    useEffect(() => {
        if (session === null || !asksMail) {
            return;
        }

        let listening = true;

        void draftsMailReplies(session, transport).then((answer) => {
            if (listening && answer.outcome === 'read') {
                setDraftsReplies(answer.value);
            }
        });

        return () => {
            listening = false;
        };
    }, [session, transport, asksMail, offered]);

    return draftsReplies;
}
