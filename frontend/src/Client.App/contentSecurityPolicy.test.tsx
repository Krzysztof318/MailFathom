// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createHash } from 'node:crypto';
import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { asHeaderValue, directivesBothHeadsShare, webHeadDirectives } from './contentSecurityPolicy';
import { LocalizationProvider } from './src/localization/Localization';
import { EmbeddedMessageMarkup } from './src/messageBody/MessageMarkupFrame';
import { LinkOpenerContext } from './src/shellOperations/linkOpener';

// The scripts are read out of the document the frame actually writes rather than imported from where they are declared,
// so what is proven is that the policy admits what reaches a `srcdoc` — a script wrapped, trimmed, or joined differently
// on the way there would be refused by the browser and would fail here first.
function scriptsTheEmbeddedFrameWrites(): string[] {
    render(
        <LocalizationProvider>
            <LinkOpenerContext value={() => Promise.resolve()}>
                <EmbeddedMessageMarkup markup="<html><head></head><body><p>As the sender wrote it.</p></body></html>" />
            </LinkOpenerContext>
        </LocalizationProvider>,
    );

    const document = screen.getByTitle("The sender's own markup, drawn in isolation").getAttribute('srcdoc') ?? '';

    return [...document.matchAll(/<script>([\s\S]*?)<\/script>/g)].map((script) => script[1] ?? '');
}

function hashSourceOf(script: string): string {
    return `'sha256-${createHash('sha256').update(script, 'utf8').digest('base64')}'`;
}

describe('contentSecurityPolicy', () => {
    it.each([
        ['that both heads share', directivesBothHeadsShare],
        ['the web head is served under', webHeadDirectives],
    ])('admits, in the directives %s, every script the embedded markup frame writes by its hash', (_, directives) => {
        const scripts = scriptsTheEmbeddedFrameWrites();

        expect(scripts).toHaveLength(2);
        expect(directives['script-src']?.split(' ')).toEqual(expect.arrayContaining(scripts.map(hashSourceOf)));
    });

    it('lets the web head connect to the origin that served it and to no other', () => {
        expect(webHeadDirectives['connect-src']).toBe("'self'");
    });

    it('writes the web head policy as one header value that no page can be framed or planted into', () => {
        const header = asHeaderValue(webHeadDirectives);

        expect(header).not.toMatch(/[\r\n]/);
        expect(header.split('; ')).toEqual(
            expect.arrayContaining(["object-src 'none'", "base-uri 'none'", "frame-ancestors 'none'"]),
        );
    });
});
