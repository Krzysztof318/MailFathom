// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ReactNode } from 'react';
import type { MailDocumentLink } from '@mailfathom/client-backend';
import Markdown, { type MarkdownToJSX } from 'markdown-to-jsx/react';
import { useLocalization } from '../../localization/useLocalization';
import { MessageLink } from '../../messageBody/MessageLink';
import {
    headingShapeAt,
    inlineCodeShape,
    orderedListShape,
    preformattedShape,
    quoteShape,
    separatorShape,
    strikethroughShape,
    strongShape,
    tableCellShape,
    tableHeaderCellShape,
    tableRegionShape,
    tableRowShape,
    tableShape,
    unorderedListShape,
} from '../../messageBody/writtenBlockShapes';

// The answer's text as the model wrote it, which may be Markdown. The design draws it with the helpers a message body
// is drawn with, so every block takes the message body's shape, and it is drawn as React elements and never as HTML.
//
// The agent reads mail anybody can send, so what could carry a sender's intent past the answer is held to what a
// message body already holds it to: raw HTML stays text, an image renders nothing and so fetches nothing, and a link
// is drawn the way a link in mail is — saying where it goes, warned about, and opened out of the application — or as
// its words alone when it names no web or mail address. A heading is drawn at the design's steps without becoming a
// heading, because the answer sits inside a screen whose heading order it cannot know.
//
// Every block is spaced by the margin below it rather than by a gap, so that the last paragraph can be drawn inline
// with the citations that follow it without losing the space above it.

const spaced = 'not-last:mb-3';

const followedSchemes: ReadonlySet<string> = new Set(['http:', 'https:', 'mailto:']);

function linkTo(href: string | undefined): MailDocumentLink | null {
    if (href === undefined || !URL.canParse(href)) {
        return null;
    }

    const url = new URL(href);

    if (!followedSchemes.has(url.protocol)) {
        return null;
    }

    // Nobody judged this link the way the service judges one in mail, so it is always worth a warning.
    return {
        target: url.href,
        host: url.host === '' ? null : url.host,
        asciiHost: null,
        deception: 'NotApplicable',
        worthWarningAbout: true,
    };
}

interface Written {
    readonly children?: ReactNode;
}

function headingAt(level: number) {
    return function Heading({ children }: Written) {
        return <p className={`${spaced} ${headingShapeAt(level)}`}>{children}</p>;
    };
}

function Link({ href, children }: Written & { readonly href?: string }) {
    const link = linkTo(href);

    return link === null ? <>{children}</> : <MessageLink link={link}>{children}</MessageLink>;
}

function Nothing() {
    return null;
}

function Table({ children }: Written) {
    const { translate } = useLocalization();

    // Focusable and named for the reason a table in mail is: a column past the edge is reachable from a keyboard only
    // where the region that scrolls it can take focus.
    return (
        <div
            aria-label={translate('body.tableRegion')}
            className={`${spaced} ${tableRegionShape}`}
            role="group"
            tabIndex={0}
        >
            <table className={tableShape}>{children}</table>
        </div>
    );
}

function Preformatted({ children }: Written) {
    const { translate } = useLocalization();

    // The code inside is the block's text rather than an inline run, so it takes none of the inline code's frame.
    return (
        <pre
            aria-label={translate('body.preformattedRegion')}
            className={`${spaced} ${preformattedShape} [&>code]:contents`}
            role="group"
            tabIndex={0}
        >
            {children}
        </pre>
    );
}

const options: MarkdownToJSX.Options = {
    disableParsingRawHTML: true,
    forceBlock: true,
    wrapper: null,
    overrides: {
        h1: headingAt(1),
        h2: headingAt(2),
        h3: headingAt(3),
        h4: headingAt(4),
        h5: headingAt(5),
        h6: headingAt(6),
        a: Link,
        img: Nothing,
        p: { props: { className: spaced } },
        ul: { props: { className: `${spaced} ${unorderedListShape}` } },
        ol: { props: { className: `${spaced} ${orderedListShape}` } },
        blockquote: { props: { className: `${spaced} ${quoteShape}` } },
        pre: Preformatted,
        code: { props: { className: inlineCodeShape } },
        table: Table,
        tr: { props: { className: tableRowShape } },
        th: { props: { className: `${tableCellShape} ${tableHeaderCellShape}` } },
        td: { props: { className: tableCellShape } },
        hr: { props: { className: `${spaced} ${separatorShape}` } },
        del: { props: { className: strikethroughShape } },
        strong: { props: { className: strongShape } },
    },
};

export function AnswerText({ text }: { readonly text: string }) {
    return <Markdown options={options}>{text}</Markdown>;
}
