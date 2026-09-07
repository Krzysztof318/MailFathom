// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// One message as a reading pane draws it: what the message route answers around a body, what the body route answers as
// the body, the sender's own markup behind that, and the octets of the one file it carries.
//
// **Every body here carries a `plainText` object, and a `null` there is refused rather than tolerated.** The body route
// answers both renderings together — the document, and the same message as words — because a pane needs the second
// whenever the first is refused, so `plainText` is not an alternative representation the sender may have omitted: it is
// what MailFathom derived, present on every readable body whatever the sender sent. A fixture writing `null` into it is
// refused by `mailBody.ts` before any screen sees it, and what a reader then meets is the pane saying the message could
// not be read — which reads as a defect in the client and is a malformed fixture. {@link markupOnlyMessage} below is the
// case that would tempt one: a sender who wrote no text part at all, whose message route answer says exactly that and
// whose body still carries the words.

/** The identity the newsletter is addressed by, everywhere the corpus names one message. */
export const newsletterId = '00000000-0000-4000-8000-000000000000';

/** @see markupOnlyMessage */
export const markupOnlyId = '00000000-0000-4000-8000-000000000001';

/** The subject the newsletter carries, which is what names the region a pane draws it in. */
export const newsletterSubject = 'A newsletter from Example';

/** The sender's own first-level heading inside the newsletter, which a pane draws deeper than they wrote it. */
export const newsletterHeading = 'This week at Example';

/** @see markupOnlyMessage */
export const markupOnlySubject = 'Your order is on its way';

/** What the one attached file holds — small enough to state here, and large enough to arrive as more than nothing. */
export const attachedOctets = 'order,quantity\nkettle,1\n';

/** A one-pixel transparent picture, standing in for a part a message carried itself rather than asked to be fetched. */
export const transparentPicture = 'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7';

/** The host a message asks its pictures to be fetched from, which is what a check watching for a leak watches for. */
export const senderPictureHost = 'pictures.invalid';

/** The address of the picture the newsletter asks for, retained only where the reader asked for the sender's pictures. */
export const senderPicture = `https://${senderPictureHost}/mark.png`;

/** The host the script inside the sender's markup fetches from, so a request to it is that script having run. */
export const senderScriptHost = 'ranscript.invalid';

function run(text: string, overrides: Readonly<Record<string, unknown>> = {}) {
    return { text, emphasis: 'None', foreground: null, link: null, ...overrides };
}

/**
 * The newsletter as a closed document tree.
 *
 * The blocks are chosen so each identity the contract carries is drawn at least once, and so the two things a sender
 * may try are both visible: markup written as text, and a link whose words name one place while its target names
 * another.
 *
 * @param pictureSource What the picture block resolves to — the message's own inlined copy, or the sender's address
 * where the reader asked for it.
 */
export function newsletterBlocks(pictureSource: string) {
    return [
        { type: 'heading', version: 1, level: 1, content: [run(newsletterHeading)], alignment: 'Start' },
        {
            type: 'paragraph',
            version: 1,
            content: [
                run('A sender may write '),
                run('<script>alert(1)</script>', { emphasis: 'Bold, Monospace' }),
                run(' and it stays words.'),
            ],
            alignment: 'Inherited',
        },
        {
            type: 'paragraph',
            version: 1,
            content: [
                run('example.invalid', {
                    link: {
                        target: 'https://offers.invalid/claim',
                        host: 'offers.invalid',
                        asciiHost: null,
                        deception: 'DisplayedHostDiffers',
                        isWorthWarningAbout: true,
                    },
                }),
            ],
            alignment: 'Inherited',
        },
        {
            type: 'image',
            version: 1,
            image: { source: pictureSource, alternativeText: 'The Example mark', width: 32, height: 32 },
            link: null,
            alignment: 'Center',
        },
        {
            type: 'quote',
            version: 1,
            depth: 1,
            blocks: [
                { type: 'paragraph', version: 1, content: [run('You wrote: send me the list.')], alignment: 'Start' },
            ],
        },
        { type: 'separator', version: 1 },
        { type: 'preformatted', version: 1, text: '  order  quantity\n  kettle 1' },
    ];
}

/**
 * The sender's own markup, as the self-contained representation serves it: pictures inlined and remote addresses gone,
 * unless the reader asked for this one message's pictures.
 *
 * It carries a script deliberately, which the representation itself never would. What that stands in for is the second
 * mechanism ADR 0024 keeps on this surface — the frame permits no script whatever the markup holds, so a representation
 * that ever stopped removing one would still not run it. The script fetches from a host of its own, so a browser says
 * whether it ran without anything having to read inside a frame it cannot reach into.
 */
export function senderMarkup(remoteImages: boolean): string {
    const picture = remoteImages ? senderPicture : transparentPicture;

    return (
        `<html><body><h1>${newsletterHeading}</h1>` +
        `<script>new Image().src = "https://${senderScriptHost}/beacon.png";</script>` +
        `<img src="${picture}" alt="A mark">` +
        '</body></html>'
    );
}

/**
 * What the body route answers for the newsletter.
 *
 * Both flags are questions the reader asked rather than anything the corpus decides, which is why they are parameters:
 * asking for the sender's pictures restores the addresses the representation had removed, and asking for their own
 * markup adds a third rendering beside the two every read carries. Nothing here reads a request — a consumer that
 * routes this decides what was asked and says so.
 */
export function newsletterBody({ remoteImages = false, fullHtml = false } = {}) {
    return {
        storedEmailId: newsletterId,
        availability: 'Readable',
        plainText: {
            text: 'A newsletter, as words.\n\nRead it at example.invalid.',
            originalCharacterCount: 48,
            truncation: 'None',
        },
        document: {
            schemaVersion: 1,
            blocks: newsletterBlocks(remoteImages ? senderPicture : transparentPicture),
            refusal: 'None',
            removedRemoteReferenceCount: remoteImages ? 0 : 3,
            retainedRemoteImageCount: remoteImages ? 1 : 0,
            inlineImageCount: remoteImages ? 0 : 1,
            undrawnInlineImageCount: 0,
            truncated: false,
        },
        selfContainedHtml: fullHtml
            ? { text: senderMarkup(remoteImages), originalCharacterCount: 320, truncation: 'None' }
            : null,
        remoteImagesRequested: remoteImages,
    };
}

/** What the message route answers for the newsletter: everything a pane draws around a body it never carries. */
export const newsletterMessage = {
    storedEmailId: newsletterId,
    account: 'work',
    folder: 'INBOX',
    threadId: null,
    sizeOctets: 40_960,
    headers: {
        subject: newsletterSubject,
        sentAt: '2026-08-31T09:41:00+00:00',
        receivedAt: '2026-08-31T09:41:10+00:00',
        participants: [
            { role: 'From', address: 'news@example.invalid', displayName: 'Example' },
            { role: 'To', address: 'reader@example.invalid', displayName: null },
        ],
        messageId: 'abc@example.invalid',
        inReplyTo: null,
        references: [],
    },
    body: { availability: 'Readable', plainText: true, html: true },
    sender: { authorAuthentication: 'Authenticated', deploymentTrust: 'Unknown', authenticatedDomain: null },
    attachments: [
        {
            position: 0,
            fileName: 'orders.csv',
            wasFileNameNormalized: false,
            mediaType: 'text/csv',
            sizeOctets: attachedOctets.length,
        },
    ],
    carried: null,
    unread: true,
    flagged: false,
    answered: false,
};

/**
 * A message whose sender wrote no plain text part at all, which is the state the module note above is about.
 *
 * `body.plainText` is `false` here because that field says what the *message* carried, and this one carried markup and
 * nothing else. What the body route answers for it still carries a `plainText` object — {@link markupOnlyBody} — because
 * that is what MailFathom derived from the markup rather than what the sender attached.
 */
export const markupOnlyMessage = {
    ...newsletterMessage,
    storedEmailId: markupOnlyId,
    sizeOctets: 12_288,
    headers: {
        ...newsletterMessage.headers,
        subject: markupOnlySubject,
        sentAt: '2026-08-30T14:20:00+00:00',
        receivedAt: '2026-08-30T14:20:06+00:00',
        participants: [
            { role: 'From', address: 'dispatch@kettles.invalid', displayName: 'Kettles' },
            { role: 'To', address: 'reader@example.invalid', displayName: null },
        ],
        messageId: 'def@kettles.invalid',
    },
    body: { availability: 'Readable', plainText: false, html: true },
    attachments: [],
    unread: false,
};

/** @see markupOnlyMessage */
export const markupOnlyBody = {
    storedEmailId: markupOnlyId,
    availability: 'Readable',
    plainText: {
        text: 'Your order left this morning and should reach you on Thursday.',
        originalCharacterCount: 62,
        truncation: 'None',
    },
    document: {
        schemaVersion: 1,
        blocks: [
            {
                type: 'paragraph',
                version: 1,
                content: [run('Your order left this morning and should reach you on Thursday.')],
                alignment: 'Inherited',
            },
        ],
        refusal: 'None',
        removedRemoteReferenceCount: 0,
        retainedRemoteImageCount: 0,
        inlineImageCount: 0,
        undrawnInlineImageCount: 0,
        truncated: false,
    },
    selfContainedHtml: null,
    remoteImagesRequested: false,
};
