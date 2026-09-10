// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import {
    declareMailFolder,
    folderDeclarationRoute,
    folderRemovalRoute,
    folderReplacementRoute,
    readFolderConfigurationVersion,
    replaceMailFolder,
    withdrawMailFolder,
} from './mailFolderConfiguration';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

type Answer = Omit<ClientResponse, 'headers'>;

function answering(response: Answer): MailFathomTransport {
    return () => Promise.resolve({ ...response, headers: {} });
}

function recording(response: Answer): { transport: MailFathomTransport; requests: ClientRequest[] } {
    const requests: ClientRequest[] = [];

    return {
        requests,
        transport: (request) => {
            requests.push(request);

            return Promise.resolve({ ...response, headers: {} });
        },
    };
}

const committed: Answer = { status: 200, body: JSON.stringify({ committed: true, version: 8, messages: [] }) };

const folder = { alias: 'PROJECTS/2027', remotePath: ['INBOX', 'Projects', '2027'] };

function sent(requests: readonly ClientRequest[]): Readonly<Record<string, unknown>> {
    return JSON.parse(requests[0]?.body ?? '{}') as Readonly<Record<string, unknown>>;
}

describe('readFolderConfigurationVersion', () => {
    it('asks the record route and answers the version it stands at', async () => {
        const { transport, requests } = recording({ status: 200, body: JSON.stringify({ version: 7 }) });
        const answer = await readFolderConfigurationVersion(session, transport);

        expect(requests[0]?.method).toBe('GET');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/record');
        expect(answer).toEqual({ outcome: 'read', value: 7 });
    });

    it('refuses a version that is not a whole count of writes, which is an answer no deployment produced', async () => {
        const answer = await readFolderConfigurationVersion(
            session,
            answering({ status: 200, body: JSON.stringify({ version: 1.5 }) }),
        );

        expect(answer.outcome).toBe('failed');
    });

    it('reports a credential that may not read the record as unauthorized rather than as nothing there', async () => {
        const answer = await readFolderConfigurationVersion(session, answering({ status: 403, body: '' }));

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'unauthorized', status: 403 } });
    });
});

describe('declareMailFolder', () => {
    it('states the folder as a configuration file would, at the folders route, over the version it was composed on', async () => {
        const { transport, requests } = recording(committed);

        await declareMailFolder(session, transport, { accountId: 'work', folder, version: 7 });

        expect(requests[0]?.method).toBe('POST');
        expect(requests[0]?.path).toBe(`https://mail.example.invalid/api/client${folderDeclarationRoute}`);
        expect(sent(requests)).toEqual({
            version: 7,
            accountId: 'work',
            folder: JSON.stringify({
                Alias: 'PROJECTS/2027',
                RemotePath: 'INBOX/Projects/2027',
                CreateIfMissing: true,
            }),
        });
    });

    it('answers the version now in force, which is what the next change is composed over', async () => {
        const answer = await declareMailFolder(session, answering(committed), {
            accountId: 'work',
            folder,
            version: 7,
        });

        expect(answer).toEqual({ outcome: 'read', value: { committed: true, version: 8, messages: [] } });
    });

    it('reads a refusal as a value with the deployment’s own sentences on it, rather than as a failure', async () => {
        const refused: Answer = {
            status: 200,
            body: JSON.stringify({ committed: false, version: 7, messages: ['The record moved on.'] }),
        };

        const answer = await declareMailFolder(session, answering(refused), { accountId: 'work', folder, version: 6 });

        expect(answer).toEqual({
            outcome: 'read',
            value: { committed: false, version: 7, messages: ['The record moved on.'] },
        });
    });

    it('refuses an answer whose messages are not sentences, because a screen renders them', async () => {
        const answer = await declareMailFolder(
            session,
            answering({ status: 200, body: JSON.stringify({ committed: false, version: 7, messages: [12] }) }),
            { accountId: 'work', folder, version: 7 },
        );

        expect(answer.outcome).toBe('failed');
    });

    it('reports a deployment that did not answer at all as unavailable', async () => {
        // A transport that cannot reach the deployment throws, which is what `send` reads as no answer at all.
        const unreachable: MailFathomTransport = () => Promise.reject(new Error('no route'));
        const answer = await declareMailFolder(session, unreachable, {
            accountId: 'work',
            folder,
            version: 7,
        });

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });
});

describe('replaceMailFolder', () => {
    it('names the alias standing today beside the folder that replaces it', async () => {
        const { transport, requests } = recording(committed);

        await replaceMailFolder(session, transport, {
            accountId: 'work',
            alias: 'PROJECTS/2026',
            folder,
            version: 7,
        });

        expect(requests[0]?.path).toBe(`https://mail.example.invalid/api/client${folderReplacementRoute}`);
        expect(sent(requests)['alias']).toBe('PROJECTS/2026');
    });
});

describe('withdrawMailFolder', () => {
    it('names the alias alone, there being no folder to state afresh', async () => {
        const { transport, requests } = recording(committed);

        await withdrawMailFolder(session, transport, { accountId: 'work', alias: 'PROJECTS/2027', version: 7 });

        expect(requests[0]?.path).toBe(`https://mail.example.invalid/api/client${folderRemovalRoute}`);
        expect(sent(requests)).toEqual({ version: 7, accountId: 'work', alias: 'PROJECTS/2027' });
    });
});
