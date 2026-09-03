// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { MailAttachment } from '@mailfathom/client-backend';
import { attachmentKey, OpenAttachmentContext, useOpenAttachment } from './openAttachment';

const chart: MailAttachment = {
    position: 3,
    fileName: 'figures.png',
    wasFileNameNormalized: false,
    mediaType: 'image/png',
    sizeOctets: 2_048,
};

const reached = 'Reached the operation.';

function Opening() {
    useOpenAttachment();

    return <p>{reached}</p>;
}

describe('attachmentKey', () => {
    it('identifies a file by its place in the message rather than by what it is called', () => {
        expect(attachmentKey({ storedEmailId: 'message-1', attachment: chart })).toBe('message-1:3');
    });

    it('tells two files a sender gave the same name apart', () => {
        const first = attachmentKey({ storedEmailId: 'message-1', attachment: chart });
        const second = attachmentKey({ storedEmailId: 'message-1', attachment: { ...chart, position: 4 } });

        expect(first).not.toBe(second);
    });

    it('tells the same position in two messages apart', () => {
        const here = attachmentKey({ storedEmailId: 'message-1', attachment: chart });
        const there = attachmentKey({ storedEmailId: 'message-2', attachment: chart });

        expect(here).not.toBe(there);
    });
});

describe('useOpenAttachment', () => {
    it('answers the operation the application supplied', () => {
        render(
            <OpenAttachmentContext value={() => undefined}>
                <Opening />
            </OpenAttachmentContext>,
        );

        expect(screen.getByText(reached)).toBeDefined();
    });

    it('refuses to be reached where the application supplied none, rather than opening nothing silently', () => {
        // React reports the error it caught as well as rethrowing it, which is noise about a failure this test is
        // producing on purpose.
        const reported = vi.spyOn(console, 'error').mockImplementation(() => undefined);

        expect(() => render(<Opening />)).toThrow(/OpenAttachmentContext/u);

        reported.mockRestore();
    });
});
