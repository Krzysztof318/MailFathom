// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ReactNode } from 'react';
import Markdown, { type MarkdownToJSX } from 'markdown-to-jsx/react';

// The answer's text as the model wrote it, which may be Markdown. It is drawn as React elements and never as HTML, and
// three things a model can write are refused because each reaches past the answer: raw HTML stays text, an image
// fetches nothing, and a link is drawn as its words alone — the agent reads mail anybody can send, so a URL in its
// answer is exactly what an attacker would put there. A heading becomes a bold line rather than a heading, because the
// answer sits inside a screen whose heading order it cannot know.
//
// Every block is spaced by the margin below it rather than above it, so that the last paragraph can be drawn inline
// with the citations that follow it without losing the space before it.

const spaced = 'not-last:mb-2';

function Heading({ children }: { readonly children?: ReactNode }) {
    return <p className={`${spaced} font-semibold`}>{children}</p>;
}

function Words({ children }: { readonly children?: ReactNode }) {
    return <>{children}</>;
}

function Nothing() {
    return null;
}

const options: MarkdownToJSX.Options = {
    disableParsingRawHTML: true,
    forceBlock: true,
    wrapper: null,
    overrides: {
        h1: Heading,
        h2: Heading,
        h3: Heading,
        h4: Heading,
        h5: Heading,
        h6: Heading,
        a: Words,
        img: Nothing,
        p: { props: { className: spaced } },
        ul: { props: { className: `${spaced} list-disc ps-5` } },
        ol: { props: { className: `${spaced} list-decimal ps-5` } },
        blockquote: { props: { className: `${spaced} border-s-2 border-s-line ps-3 text-muted` } },
        pre: { props: { className: `${spaced} overflow-x-auto rounded-md bg-sunken px-3 py-2` } },
        code: { props: { className: 'font-mono text-sm' } },
        table: { props: { className: `${spaced} block overflow-x-auto text-sm` } },
        th: { props: { className: 'border border-line px-2 py-1 text-start font-semibold' } },
        td: { props: { className: 'border border-line px-2 py-1' } },
        hr: { props: { className: `${spaced} border-line` } },
    },
};

export function AnswerText({ text }: { readonly text: string }) {
    return <Markdown options={options}>{text}</Markdown>;
}
