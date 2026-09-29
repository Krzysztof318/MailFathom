// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it, vi } from 'vitest';
import { reportingRefusedCredential } from './credentialRefusal';
import type { ClientRequest, ClientResponse } from './transport';

const held = 'Bearer mfs_held';

function request(authorization: string): ClientRequest {
    return { method: 'GET', path: '/api/client/emails', headers: { Authorization: authorization } };
}

function answering(status: number): () => Promise<ClientResponse> {
    return () => Promise.resolve({ status, body: '', headers: {} });
}

describe('reportingRefusedCredential', () => {
    it('reports a refusal of the credential held now, and hands the answer on as it came', async () => {
        const refused = vi.fn();
        const transport = reportingRefusedCredential(answering(401), () => held, refused);

        expect(await transport(request(held))).toStrictEqual({ status: 401, body: '', headers: {} });
        expect(refused).toHaveBeenCalledTimes(1);
    });

    it('reports nothing for a refusal of a credential the caller has since replaced', async () => {
        const refused = vi.fn();
        const transport = reportingRefusedCredential(answering(401), () => 'Bearer mfs_renewed', refused);

        await transport(request(held));

        expect(refused).not.toHaveBeenCalled();
    });

    it.each([200, 403, 404, 503])(
        'reports nothing for an answer that is not a refused credential: %i',
        async (status) => {
            const refused = vi.fn();
            const transport = reportingRefusedCredential(answering(status), () => held, refused);

            await transport(request(held));

            expect(refused).not.toHaveBeenCalled();
        },
    );
});
