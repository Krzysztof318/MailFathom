// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import { mailBodyRoute, readMailBody } from './mailBody';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const storedEmailId = '2f1c6a5e-9b3d-4d2f-9f1a-6c0f5d8e4a31';

const noEmphasis = {
    bold: false,
    italic: false,
    underline: false,
    strikethrough: false,
    monospace: false,
};

function run(text: string, overrides: Readonly<Record<string, unknown>> = {}) {
    return { text, emphasis: 'None', foreground: null, link: null, ...overrides };
}

function paragraph(...content: readonly unknown[]) {
    return { type: 'paragraph', version: 1, content, alignment: 'Inherited' };
}

function bodyWith(document: unknown, overrides: Readonly<Record<string, unknown>> = {}): string {
    return JSON.stringify({
        storedEmailId,
        availability: 'Readable',
        plainText: { text: 'The message as words.', originalCharacterCount: 21, truncation: 'None' },
        document,
        remoteImagesRequested: false,
        ...overrides,
    });
}

function documentWith(blocks: readonly unknown[], overrides: Readonly<Record<string, unknown>> = {}) {
    return {
        schemaVersion: 1,
        blocks,
        refusal: 'None',
        removedRemoteReferenceCount: 0,
        retainedRemoteImageCount: 0,
        inlineImageCount: 0,
        undrawnInlineImageCount: 0,
        truncated: false,
        ...overrides,
    };
}

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

async function readingDocument(blocks: readonly unknown[], remoteImages = false) {
    const document = documentWith(blocks);
    const body = bodyWith(document, { remoteImagesRequested: remoteImages });

    return readMailBody(session, answering({ status: 200, body }), storedEmailId, remoteImages);
}

async function refusing(blocks: readonly unknown[], remoteImages = false) {
    const result = await readingDocument(blocks, remoteImages);

    return result.outcome;
}

describe('mailBodyRoute', () => {
    it('asks for the tree alone when the reader has not asked for remote pictures', () => {
        expect(mailBodyRoute(storedEmailId, false)).toBe(`/messages/${storedEmailId}/body`);
    });

    it('carries the reader ask for remote pictures as the whole of that state', () => {
        expect(mailBodyRoute(storedEmailId, true)).toBe(`/messages/${storedEmailId}/body?remoteImages=true`);
    });

    it('escapes an identifier rather than writing it into the path as it arrived', () => {
        expect(mailBodyRoute('../accounts', false)).toBe('/messages/..%2Faccounts/body');
    });
});

describe('readMailBody', () => {
    it('asks for the body route on the client surface with the session it was given', async () => {
        const { transport, requests } = recording({ status: 200, body: bodyWith(documentWith([])) });

        await readMailBody(session, transport, storedEmailId, false);

        expect(requests).toEqual([
            {
                method: 'GET',
                path: `https://mail.example.invalid/api/client/messages/${storedEmailId}/body`,
                headers: { Accept: 'application/json', Authorization: 'Basic dGVzdA==' },

                // A body carrying the pictures the service will compose is larger than the backstop written for an
                // address nobody has trusted, so this operation states the ceiling it actually needs.
                longestAnswer: 8 * 1024 * 1024,
            },
        ]);
    });

    it('reads the body a well-formed answer describes', async () => {
        const result = await readingDocument([paragraph(run('Hello.'))]);

        expect(result).toEqual({
            outcome: 'read',
            value: {
                storedEmailId,
                availability: 'Readable',
                plainText: { text: 'The message as words.', originalCharacterCount: 21, truncation: 'None' },
                document: {
                    blocks: [
                        {
                            type: 'paragraph',
                            alignment: 'Inherited',
                            content: [{ text: 'Hello.', emphasis: noEmphasis, foreground: null, link: null }],
                        },
                    ],
                    refusal: 'None',
                    removedRemoteReferenceCount: 0,
                    retainedRemoteImageCount: 0,
                    inlineImageCount: 0,
                    undrawnInlineImageCount: 0,
                    truncated: false,
                },
                remoteImagesRequested: false,
            },
        });
    });

    it('reads a refused document as the refusal beside the plain text it falls back to', async () => {
        const document = documentWith([], { refusal: 'NoHtmlPart' });
        const body = bodyWith(document);

        const result = await readMailBody(session, answering({ status: 200, body }), storedEmailId, false);

        expect(result.outcome === 'read' && result.value.document?.refusal).toBe('NoHtmlPart');
    });

    it.each([
        [401, 'unauthenticated'],
        [403, 'unauthorized'],
        [404, 'unavailable'],
        [500, 'unavailable'],
    ])('reads status %i as the failure a screen acts on', async (status, reason) => {
        const result = await readMailBody(session, answering({ status, body: '' }), storedEmailId, false);

        expect(result).toEqual({ outcome: 'failed', failure: { reason, status } });
    });

    it('reports a deployment nothing answered from as unavailable, rather than throwing at its caller', async () => {
        const refusing: MailFathomTransport = () => Promise.reject(new Error('the deployment is not there'));

        const result = await readMailBody(session, refusing, storedEmailId, false);

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });

    it('refuses a body that is not JSON rather than reading it as an empty message', async () => {
        const result = await readMailBody(session, answering({ status: 200, body: 'not json' }), storedEmailId, false);

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('reads a message the deployment holds nothing readable for as that state', async () => {
        const body = bodyWith(null, { availability: 'EncryptedNotReadableLocally' });

        const result = await readMailBody(session, answering({ status: 200, body }), storedEmailId, false);

        expect(result.outcome === 'read' && result.value).toMatchObject({
            availability: 'EncryptedNotReadableLocally',
            document: null,
        });
    });
});

describe('readMailBody refusing a document this deployment did not compose', () => {
    it('refuses a document written against a schema revision this build does not implement', async () => {
        const body = bodyWith(documentWith([], { schemaVersion: 2 }));

        const result = await readMailBody(session, answering({ status: 200, body }), storedEmailId, false);

        expect(result.outcome).toBe('failed');
    });

    it.each([
        ['a colour in any notation but the one the wire writes', paragraph(run('x', { foreground: 'red' }))],
        ['a colour of the wrong length', paragraph(run('x', { foreground: '#fff' }))],
        ['an emphasis flag outside the set', paragraph(run('x', { emphasis: 'Bold, Blink' }))],
        ['an emphasis that is not a string', paragraph(run('x', { emphasis: 3 }))],
        ['text that is not a string', paragraph({ text: 7, emphasis: 'None', foreground: null, link: null })],
        ['an alignment outside the set', { type: 'paragraph', version: 1, content: [], alignment: 'Middle' }],
        [
            'a heading above the deepest level',
            { type: 'heading', version: 1, level: 7, content: [], alignment: 'Start' },
        ],
        [
            'a heading below the shallowest level',
            { type: 'heading', version: 1, level: 0, content: [], alignment: 'Start' },
        ],
        ['a quote at no depth', { type: 'quote', version: 1, depth: 0, blocks: [] }],
        ['a block that is not an object', 'paragraph'],
        ['a block naming no type', { version: 1, content: [], alignment: 'Start' }],
    ])('refuses %s', async (_, block) => {
        expect(await refusing([block])).toBe('failed');
    });

    it.each([
        ['javascript:', 'javascript:alert(1)'],
        ['data:', 'data:text/html,<script>alert(1)</script>'],
        ['file:', 'file:///etc/passwd'],
        ['a relative reference', '/somewhere'],
    ])('refuses a link target carrying %s', async (_, target) => {
        const link = { target, host: null, asciiHost: null, deception: 'NotApplicable', isWorthWarningAbout: false };

        expect(await refusing([paragraph(run('Click', { link }))])).toBe('failed');
    });

    it.each([
        ['http', 'http://example.invalid/a'],
        ['https', 'https://example.invalid/a'],
        ['mailto', 'mailto:someone@example.invalid'],
    ])('admits a link target carrying %s', async (_, target) => {
        const link = { target, host: null, asciiHost: null, deception: 'NotApplicable', isWorthWarningAbout: false };

        expect(await refusing([paragraph(run('Click', { link }))])).toBe('read');
    });

    it('refuses a link whose deception the service did not state', async () => {
        const link = { target: 'https://example.invalid/', host: null, asciiHost: null, deception: 'Maybe' };

        expect(await refusing([paragraph(run('Click', { link }))])).toBe('failed');
    });

    it('refuses a link the service did not judge worth warning about either way', async () => {
        const link = {
            target: 'https://example.invalid/',
            host: 'example.invalid',
            asciiHost: null,
            deception: 'None',
        };

        expect(await refusing([paragraph(run('Click', { link }))])).toBe('failed');
    });

    it('refuses a picture whose source is a document rather than an image', async () => {
        const image = {
            source: 'data:text/html;base64,PHNjcmlwdD4=',
            alternativeText: null,
            width: null,
            height: null,
        };

        expect(await refusing([{ type: 'image', version: 1, image, link: null, alignment: 'Start' }])).toBe('failed');
    });

    it('refuses a remote picture on a read the reader did not ask pictures for', async () => {
        const image = { source: 'https://tracker.invalid/pixel.gif', alternativeText: null, width: null, height: null };

        expect(await refusing([{ type: 'image', version: 1, image, link: null, alignment: 'Start' }])).toBe('failed');
    });

    it('admits a remote picture on the read the reader did ask pictures for', async () => {
        const image = {
            source: 'https://newsletter.invalid/banner.png',
            alternativeText: 'A banner',
            width: 600,
            height: 200,
        };
        const block = { type: 'image', version: 1, image, link: null, alignment: 'Center' };

        expect(await refusing([block], true)).toBe('read');
    });

    it('refuses an answer composed for a different request than the one that was made', async () => {
        const body = bodyWith(documentWith([]), { remoteImagesRequested: true });

        const result = await readMailBody(session, answering({ status: 200, body }), storedEmailId, false);

        expect(result.outcome).toBe('failed');
    });

    it('refuses a document nested deeper than the reduction composes', async () => {
        let block: unknown = paragraph(run('At the bottom.'));
        for (let depth = 0; depth < 24; depth += 1) {
            block = { type: 'quote', version: 1, depth: 1, blocks: [block] };
        }

        expect(await refusing([block])).toBe('failed');
    });

    it('refuses a document holding more blocks than one reduction emits', async () => {
        const blocks = Array.from({ length: 4001 }, () => paragraph(run('x')));

        expect(await refusing(blocks)).toBe('failed');
    });

    it('refuses a paragraph holding more runs than one block carries', async () => {
        const runs = Array.from({ length: 513 }, () => run('x'));

        expect(await refusing([paragraph(...runs)])).toBe('failed');
    });

    it('refuses a run longer than one run may be', async () => {
        expect(await refusing([paragraph(run('x'.repeat(20_001)))])).toBe('failed');
    });

    it('refuses a table with more rows than one table holds', async () => {
        const rows = Array.from({ length: 1001 }, () => ({ isHeader: false, cells: [] }));

        expect(await refusing([{ type: 'table', version: 1, columns: [], rows }])).toBe('failed');
    });

    it('refuses a table declaring more columns than a row can be wide', async () => {
        const columns = Array.from({ length: 65 }, () => ({ widthShare: null }));

        expect(await refusing([{ type: 'table', version: 1, columns, rows: [] }])).toBe('failed');
    });

    it('refuses a table row with more cells than one row holds', async () => {
        const cells = Array.from({ length: 65 }, () => ({
            columnSpan: 1,
            rowSpan: 1,
            alignment: 'Start',
            background: null,
            blocks: [],
        }));

        expect(await refusing([{ type: 'table', version: 1, columns: [], rows: [{ isHeader: false, cells }] }])).toBe(
            'failed',
        );
    });

    it('refuses a column claiming a width that is not a share of its parent', async () => {
        const table = { type: 'table', version: 1, columns: [{ widthShare: 4 }], rows: [] };

        expect(await refusing([table])).toBe('failed');
    });

    it('refuses a picture larger than one picture may be', async () => {
        const encoded = 'A'.repeat(4 * (2 * 1024 * 1024) + 8);
        const image = { source: `data:image/png;base64,${encoded}`, alternativeText: null, width: null, height: null };

        expect(await refusing([{ type: 'image', version: 1, image, link: null, alignment: 'Start' }])).toBe('failed');
    });

    it('refuses a document whose pictures are each small enough and together are not', async () => {
        // Three pictures of a megabyte and a half: none of them reaches the two megabytes one picture may be, and the
        // three together pass the four a whole document may carry. Base64 carries three octets in four characters.
        const encoded = 'A'.repeat((4 * (3 * 1024 * 1024)) / 2 / 3);
        const image = { source: `data:image/png;base64,${encoded}`, alternativeText: null, width: null, height: null };
        const blocks = Array.from({ length: 3 }, () => ({
            type: 'image',
            version: 1,
            image,
            link: null,
            alignment: 'Start',
        }));

        expect(await refusing(blocks)).toBe('failed');
    });

    it('refuses a picture claiming an edge no screen draws', async () => {
        const image = { source: 'data:image/gif;base64,R0lGOD==', alternativeText: null, width: 10_001, height: 40 };

        expect(await refusing([{ type: 'image', version: 1, image, link: null, alignment: 'Start' }])).toBe('failed');
    });

    it('refuses a picture claiming an edge of nothing, which no layout reserves room for', async () => {
        const image = { source: 'data:image/gif;base64,R0lGOD==', alternativeText: null, width: 40, height: 0 };

        expect(await refusing([{ type: 'image', version: 1, image, link: null, alignment: 'Start' }])).toBe('failed');
    });

    it('refuses a picture described at greater length than a description may be', async () => {
        const image = {
            source: 'data:image/gif;base64,R0lGOD==',
            alternativeText: 'a'.repeat(1025),
            width: null,
            height: null,
        };

        expect(await refusing([{ type: 'image', version: 1, image, link: null, alignment: 'Start' }])).toBe('failed');
    });

    it('refuses a link whose target is longer than a target may be', async () => {
        const target = `https://example.invalid/${'a'.repeat(4096)}`;
        const link = {
            target,
            host: 'example.invalid',
            asciiHost: null,
            deception: 'None',
            isWorthWarningAbout: false,
        };

        expect(await refusing([paragraph(run('Follow it.', { link }))])).toBe('failed');
    });

    it('refuses a picture from the sender whose address is longer than an address may be', async () => {
        const source = `https://pictures.invalid/${'a'.repeat(4096)}`;
        const image = { source, alternativeText: null, width: null, height: null };
        const blocks = [{ type: 'image', version: 1, image, link: null, alignment: 'Start' }];

        // On the read that asked for them, which is the only read a remote address reaches this parser on at all.
        expect(await refusing(blocks, true)).toBe('failed');
    });

    it('refuses preformatted text longer than one run of text may be', async () => {
        const text = 'a'.repeat(20_001);

        expect(await refusing([{ type: 'preformatted', version: 1, text }])).toBe('failed');
    });

    it('refuses a list carrying more items than a document holds blocks', async () => {
        // An item that reduced to nothing charges nothing against the block budget, so the item count is bounded in
        // its own right. The service emits no such item, which is what makes its own count bounded transitively.
        const items = Array.from({ length: 4001 }, () => ({ blocks: [] }));

        expect(await refusing([{ type: 'list', version: 1, ordered: false, items }])).toBe('failed');
    });

    it('refuses a cell claiming more columns than a row is wide', async () => {
        const cell = { columnSpan: 65, rowSpan: 1, alignment: 'Start', background: null, blocks: [] };
        const rows = [{ isHeader: false, cells: [cell] }];

        expect(await refusing([{ type: 'table', version: 1, columns: [], rows }])).toBe('failed');
    });

    it('refuses a cell claiming more rows than a table is tall', async () => {
        const cell = { columnSpan: 1, rowSpan: 1001, alignment: 'Start', background: null, blocks: [] };
        const rows = [{ isHeader: false, cells: [cell] }];

        expect(await refusing([{ type: 'table', version: 1, columns: [], rows }])).toBe('failed');
    });
});

describe('readMailBody meeting a deployment ahead of this client', () => {
    it('draws a placeholder for a block type this build does not implement, and keeps the rest of the message', async () => {
        const blocks = [{ type: 'chart', version: 1 }, paragraph(run('After it.'))];

        const result = await readingDocument(blocks);

        expect(result.outcome === 'read' && result.value.document?.blocks).toEqual([
            { type: 'unimplemented', identity: 'chart', version: 1 },
            {
                type: 'paragraph',
                alignment: 'Inherited',
                content: [{ text: 'After it.', emphasis: noEmphasis, foreground: null, link: null }],
            },
        ]);
    });

    it('draws a placeholder for a known block at a revision this build does not implement', async () => {
        const result = await readingDocument([{ type: 'paragraph', version: 2, content: [], alignment: 'Inherited' }]);

        expect(result.outcome === 'read' && result.value.document?.blocks).toEqual([
            { type: 'unimplemented', identity: 'paragraph', version: 2 },
        ]);
    });
});

describe('readMailBody reading what the contract holds', () => {
    it('reads a message carrying more pictures than one read decodes parts for, which the service composes', async () => {
        const image = { source: 'data:image/gif;base64,R0lGOD==', alternativeText: null, width: null, height: null };
        const blocks = Array.from({ length: 65 }, () => ({
            type: 'image',
            version: 1,
            image,
            link: null,
            alignment: 'Start',
        }));

        const result = await readingDocument(blocks);

        // The service bounds how many parts a read decodes, not how many blocks the reduction emits — a newsletter
        // repeating one small logo is a document it composes, so refusing it here would cost the reader the message.
        expect(result.outcome === 'read' && result.value.document?.blocks.length).toBe(65);
    });

    it('reads a set of emphasis flags as the flags it composes', async () => {
        const result = await readingDocument([paragraph(run('Loud.', { emphasis: 'Bold, Italic' }))]);

        expect(result.outcome === 'read' && result.value.document?.blocks[0]).toEqual({
            type: 'paragraph',
            alignment: 'Inherited',
            content: [
                {
                    text: 'Loud.',
                    emphasis: { ...noEmphasis, bold: true, italic: true },
                    foreground: null,
                    link: null,
                },
            ],
        });
    });

    it('reads a quotation and what it holds at the depth the message wrote', async () => {
        const quote = { type: 'quote', version: 1, depth: 2, blocks: [paragraph(run('Quoted.'))] };

        const result = await readingDocument([quote]);

        expect(result.outcome === 'read' && result.value.document?.blocks[0]).toMatchObject({
            type: 'quote',
            depth: 2,
        });
    });

    it('reads a separator, which carries nothing but itself', async () => {
        const result = await readingDocument([{ type: 'separator', version: 1 }]);

        expect(result.outcome === 'read' && result.value.document?.blocks).toEqual([{ type: 'separator' }]);
    });

    it('reads a numbered list and what each of its items holds', async () => {
        const list = {
            type: 'list',
            version: 1,
            ordered: true,
            items: [{ blocks: [paragraph(run('First.'))] }, { blocks: [paragraph(run('Second.'))] }],
        };

        const result = await readingDocument([list]);

        expect(result.outcome === 'read' && result.value.document?.blocks[0]).toMatchObject({
            type: 'list',
            ordered: true,
            items: [{ blocks: [{ type: 'paragraph' }] }, { blocks: [{ type: 'paragraph' }] }],
        });
    });

    it('reads a table as its columns, its header row, and what its cells hold', async () => {
        const cell = {
            columnSpan: 2,
            rowSpan: 1,
            alignment: 'Center',
            background: '#0028a0',
            blocks: [paragraph(run('Total'))],
        };
        const table = {
            type: 'table',
            version: 1,
            columns: [{ widthShare: 0.5 }, { widthShare: null }],
            rows: [{ isHeader: true, cells: [cell] }],
        };

        const result = await readingDocument([table]);

        expect(result.outcome === 'read' && result.value.document?.blocks[0]).toMatchObject({
            type: 'table',
            columns: [{ widthShare: 0.5 }, { widthShare: null }],
            rows: [{ isHeader: true, cells: [{ columnSpan: 2, background: '#0028a0' }] }],
        });
    });

    it('reads preformatted text as the message wrote it, whitespace included', async () => {
        const result = await readingDocument([
            { type: 'preformatted', version: 1, text: '  two   spaces\n\tand a tab' },
        ]);

        expect(result.outcome === 'read' && result.value.document?.blocks).toEqual([
            { type: 'preformatted', text: '  two   spaces\n\tand a tab' },
        ]);
    });

    it('reads a picture the message carried itself, and where following it goes', async () => {
        const image = {
            source: 'data:image/png;base64,iVBORw0KGgo=',
            alternativeText: 'The logo',
            width: 120,
            height: 40,
        };
        const link = {
            target: 'https://example.invalid/shop',
            host: 'example.invalid',
            asciiHost: null,
            deception: 'NotApplicable',
            isWorthWarningAbout: false,
        };

        const result = await readingDocument([{ type: 'image', version: 1, image, link, alignment: 'Center' }]);

        expect(result.outcome === 'read' && result.value.document?.blocks[0]).toMatchObject({
            type: 'image',
            image: { alternativeText: 'The logo', width: 120, height: 40 },
            link: { target: 'https://example.invalid/shop', worthWarningAbout: false },
        });
    });

    it('carries the deception the service judged rather than judging it again', async () => {
        const link = {
            target: 'https://evil.invalid/',
            host: 'evil.invalid',
            asciiHost: 'xn--evl-7na.invalid',
            deception: 'DisplayedHostDiffers',
            isWorthWarningAbout: true,
        };

        const result = await readingDocument([paragraph(run('example.invalid', { link }))]);

        expect(result.outcome === 'read' && result.value.document?.blocks[0]).toMatchObject({
            content: [{ link: { deception: 'DisplayedHostDiffers', worthWarningAbout: true } }],
        });
    });
});

describe('readMailBody refusing a malformed answer', () => {
    it.each([
        ['a body that is not an object', '[]'],
        ['a document that is not an object', bodyWith(7)],
        ['a message identified by something other than a string', bodyWith(null, { storedEmailId: 7 })],
        ['an availability outside the set', bodyWith(null, { availability: 'Maybe' })],
        ['no plain text at all', bodyWith(null, { plainText: null })],
        [
            'a plain text truncation outside the set',
            bodyWith(null, { plainText: { text: '', originalCharacterCount: 0, truncation: 'Somewhat' } }),
        ],
        [
            'a plain text character count that is not a count',
            bodyWith(null, { plainText: { text: '', originalCharacterCount: -1, truncation: 'None' } }),
        ],
        ['blocks that are not a list', bodyWith(documentWith([] as unknown[], { blocks: {} }))],
        ['a count that is not a count', bodyWith(documentWith([], { inlineImageCount: 'many' }))],
        ['a truncation flag that is not a flag', bodyWith(documentWith([], { truncated: 'yes' }))],
        ['a refusal outside the set', bodyWith(documentWith([], { refusal: 'Undecided' }))],
    ])('refuses %s', async (_, body) => {
        const result = await readMailBody(session, answering({ status: 200, body }), storedEmailId, false);

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it.each([
        ['a list whose items are not a list', { type: 'list', version: 1, ordered: false, items: {} }],
        ['a list item that is not an object', { type: 'list', version: 1, ordered: false, items: ['x'] }],
        ['a list that does not say whether it numbers its items', { type: 'list', version: 1, items: [] }],
        ['a table whose rows are not a list', { type: 'table', version: 1, columns: [], rows: {} }],
        ['a table column that is not an object', { type: 'table', version: 1, columns: ['wide'], rows: [] }],
        [
            'a table row that does not say whether it labels the columns',
            { type: 'table', version: 1, columns: [], rows: [{ cells: [] }] },
        ],
        [
            'a cell covering no column',
            {
                type: 'table',
                version: 1,
                columns: [],
                rows: [
                    {
                        isHeader: false,
                        cells: [{ columnSpan: 0, rowSpan: 1, alignment: 'Start', background: null, blocks: [] }],
                    },
                ],
            },
        ],
        [
            'a cell painted in a notation the wire does not write',
            {
                type: 'table',
                version: 1,
                columns: [],
                rows: [
                    {
                        isHeader: false,
                        cells: [{ columnSpan: 1, rowSpan: 1, alignment: 'Start', background: 'navy', blocks: [] }],
                    },
                ],
            },
        ],
        ['preformatted text that is not text', { type: 'preformatted', version: 1, text: 42 }],
        [
            'a picture with no source',
            { type: 'image', version: 1, image: { alternativeText: null }, link: null, alignment: 'Start' },
        ],
        [
            'a picture sized by something other than a number',
            {
                type: 'image',
                version: 1,
                image: {
                    source: 'data:image/png;base64,iVBORw0KGgo=',
                    alternativeText: null,
                    width: 'wide',
                    height: null,
                },
                link: null,
                alignment: 'Start',
            },
        ],
        [
            'a picture whose alternative text is not text',
            {
                type: 'image',
                version: 1,
                image: { source: 'data:image/png;base64,iVBORw0KGgo=', alternativeText: 3, width: null, height: null },
                link: null,
                alignment: 'Start',
            },
        ],
        ['a run that is not an object', paragraph('Hello.')],
        ['a paragraph whose content is not a list', { type: 'paragraph', version: 1, content: {}, alignment: 'Start' }],
        ['a link that is neither absent nor an object', paragraph(run('x', { link: 'https://example.invalid/' }))],
    ])('refuses %s', async (_, block) => {
        expect(await refusing([block])).toBe('failed');
    });
});
