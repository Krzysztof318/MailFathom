// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord, isRecord } from './json';
import { headersFor, routeFor, type ClientSession } from './session';
import { send, type MailFathomTransport } from './transport';

// The client's trust boundary for a message body, which is a validating parser rather than a filter. What the service
// serves is a closed tree — every leaf is text, a number, an opaque colour, or a member of a fixed set — so nothing
// here sanitizes anything: it establishes that what arrived is a document this deployment produced, and refuses the
// whole of it when it is not. ADR 0024 records why that is the boundary and what each of its parts holds.

/** The route one message's body is served at, relative to the client prefix. */
export function mailBodyRoute(storedEmailId: string, remoteImages: boolean): string {
    const asked = remoteImages ? '?remoteImages=true' : '';

    return `/messages/${encodeURIComponent(storedEmailId)}/body${asked}`;
}

/** Whether the body could be read at all, or why the deployment holds nothing to draw. */
export type MailBodyAvailability =
    'Readable' | 'EncryptedNotReadableLocally' | 'NotStoredExceededSizeLimit' | 'NotStoredAwaitingStorageHeadroom';

/** Which bound cut the plain text short, or that none did. */
export type MailBodyTruncation = 'None' | 'BodyCharacterLimit' | 'ReadCharacterBudget' | 'SensitiveContentScanCeiling';

/** Why the body is read as its plain text rather than as a document, or that it is not. */
export type MailDocumentRefusal = 'None' | 'NoHtmlPart' | 'ReductionFailed' | 'NothingRenderable';

/** How a block places its content across the width it was given. */
export type MailBlockAlignment = 'Inherited' | 'Start' | 'Center' | 'End' | 'Justify';

/** What the link's own words claim about where it goes, as the service judged it. */
export type MailLinkDeception = 'NotApplicable' | 'None' | 'DisplayedHostDiffers';

/**
 * The emphasis one run carries.
 *
 * A record of flags rather than the wire's comma-joined names, because the set composes: a run is bold and italic at
 * once, and every renderer would otherwise split the same string again.
 */
export interface MailTextEmphasis {
    readonly bold: boolean;
    readonly italic: boolean;
    readonly underline: boolean;
    readonly strikethrough: boolean;
    readonly monospace: boolean;
}

/** Where a link goes, and what its own words claimed about that. */
export interface MailDocumentLink {
    readonly target: string;
    readonly host: string | null;
    readonly asciiHost: string | null;
    readonly deception: MailLinkDeception;
    readonly worthWarningAbout: boolean;
}

/** One picture the message carried itself, or one the reader asked to be fetched. */
export interface MailInlineImage {
    readonly source: string;
    readonly alternativeText: string | null;
    readonly width: number | null;
    readonly height: number | null;
}

/** One run of text inside a paragraph or a heading. */
export interface MailInlineRun {
    readonly text: string;
    readonly emphasis: MailTextEmphasis;
    readonly foreground: string | null;
    readonly link: MailDocumentLink | null;
}

export interface MailParagraphBlock {
    readonly type: 'paragraph';
    readonly content: readonly MailInlineRun[];
    readonly alignment: MailBlockAlignment;
}

export interface MailHeadingBlock {
    readonly type: 'heading';
    readonly level: number;
    readonly content: readonly MailInlineRun[];
    readonly alignment: MailBlockAlignment;
}

export interface MailListItem {
    readonly blocks: readonly MailDocumentBlock[];
}

export interface MailListBlock {
    readonly type: 'list';
    readonly ordered: boolean;
    readonly items: readonly MailListItem[];
}

export interface MailTableColumn {
    readonly widthShare: number | null;
}

export interface MailTableCell {
    readonly columnSpan: number;
    readonly rowSpan: number;
    readonly alignment: MailBlockAlignment;
    readonly background: string | null;
    readonly blocks: readonly MailDocumentBlock[];
}

export interface MailTableRow {
    readonly isHeader: boolean;
    readonly cells: readonly MailTableCell[];
}

export interface MailTableBlock {
    readonly type: 'table';
    readonly columns: readonly MailTableColumn[];
    readonly rows: readonly MailTableRow[];
}

export interface MailQuoteBlock {
    readonly type: 'quote';
    readonly depth: number;
    readonly blocks: readonly MailDocumentBlock[];
}

export interface MailImageBlock {
    readonly type: 'image';
    readonly image: MailInlineImage;
    readonly link: MailDocumentLink | null;
    readonly alignment: MailBlockAlignment;
}

export interface MailSeparatorBlock {
    readonly type: 'separator';
}

export interface MailPreformattedBlock {
    readonly type: 'preformatted';
    readonly text: string;
}

/**
 * A block this build does not draw, which is a deployment ahead of the client rather than a defect.
 *
 * It carries no discriminator the catalogue publishes, so a block type added upstream can never collide with it. What
 * the reader loses is this block; the rest of the message is drawn around it.
 */
export interface MailUnimplementedBlock {
    readonly type: 'unimplemented';
    readonly identity: string;
    readonly version: number;
}

/** One typed part of a reduced mail body, as this build reads it. */
export type MailDocumentBlock =
    | MailParagraphBlock
    | MailHeadingBlock
    | MailListBlock
    | MailTableBlock
    | MailQuoteBlock
    | MailImageBlock
    | MailSeparatorBlock
    | MailPreformattedBlock
    | MailUnimplementedBlock;

/** A message's body reduced to the tree a pane draws, and what the reduction left out. */
export interface MailDocument {
    readonly blocks: readonly MailDocumentBlock[];
    readonly refusal: MailDocumentRefusal;
    readonly removedRemoteReferenceCount: number;
    readonly retainedRemoteImageCount: number;
    readonly inlineImageCount: number;
    readonly undrawnInlineImageCount: number;
    readonly truncated: boolean;
}

/** The message as words, which is a rendering in its own right rather than only what a refusal falls back to. */
export interface MailBodyText {
    readonly text: string;
    readonly originalCharacterCount: number;
    readonly truncation: MailBodyTruncation;
}

/** One message's body, in the two renderings a reading pane draws it from. */
export interface MailBody {
    readonly storedEmailId: string;
    readonly availability: MailBodyAvailability;
    readonly plainText: MailBodyText;
    readonly document: MailDocument | null;
    readonly remoteImagesRequested: boolean;
}

// The revision of the document contract this build reads. A document written against another one has a shape this
// client cannot know, so it is refused rather than read as far as the members happen to line up.
const documentSchemaVersion = 1;

// The revision of each block's own contract this build draws. A pair absent here — an identity nobody declares, or a
// declared identity at another revision — is drawn as a placeholder, which is what versioning per block buys.
const blockRevisions: Readonly<Record<string, number>> = {
    paragraph: 1,
    heading: 1,
    list: 1,
    table: 1,
    quote: 1,
    image: 1,
    separator: 1,
    preformatted: 1,
};

// What the service will compose at most, mirrored so a document larger than that is refused rather than drawn. They
// are the values `MailDocumentBounds` states plus the target length `MailLinkReader` enforces; a bound is checked
// during the walk rather than after it, because a walk that completed has already paid for what it read.
/**
 * The most of one body answer this operation reads, in bytes.
 *
 * The transport's own backstop is written for an address nobody has trusted yet, and it is smaller than a message this
 * service will legitimately compose: `MailDocumentBounds` lets a document carry 4 MiB of pictures, which travel as
 * base64 and are therefore a third longer again, so a newsletter with two photographs in it would be cut off and
 * reported to the reader as a defect. This is that arithmetic plus room for the words around it.
 */
const longestBodyAnswer = 8 * 1024 * 1024;

const bounds = {
    maximumDepth: 24,
    maximumBlocks: 4000,
    maximumRunsPerBlock: 512,
    maximumCharactersPerRun: 20_000,
    maximumTableRows: 1000,
    maximumTableCells: 64,
    maximumInlineImageOctets: 2 * 1024 * 1024,
    maximumInlineImageOctetsPerDocument: 4 * 1024 * 1024,
    maximumLinkTargetLength: 4096,
    maximumPictureEdge: 10_000,
    maximumAlternativeTextLength: 1024,
} as const;

const availabilities: readonly MailBodyAvailability[] = [
    'Readable',
    'EncryptedNotReadableLocally',
    'NotStoredExceededSizeLimit',
    'NotStoredAwaitingStorageHeadroom',
];

const truncations: readonly MailBodyTruncation[] = [
    'None',
    'BodyCharacterLimit',
    'ReadCharacterBudget',
    'SensitiveContentScanCeiling',
];

const refusals: readonly MailDocumentRefusal[] = ['None', 'NoHtmlPart', 'ReductionFailed', 'NothingRenderable'];

const alignments: readonly MailBlockAlignment[] = ['Inherited', 'Start', 'Center', 'End', 'Justify'];

const deceptions: readonly MailLinkDeception[] = ['NotApplicable', 'None', 'DisplayedHostDiffers'];

// The three schemes a link may carry. An allow-list rather than a list of what to refuse, which is the same set
// `MailLinkReader` admits: what an operating-system opener will act on is decided by the platform rather than here, so
// what may reach one has to be the set somebody chose.
const followableSchemes: readonly string[] = ['http://', 'https://', 'mailto:'];

// The media types a picture may arrive as, which is the list the service will compose. `data:text/html` is a document
// somebody else wrote, so the narrowness is the whole of the safety rather than an optimization.
const drawableImagePrefixes: readonly string[] = [
    'data:image/png;base64,',
    'data:image/jpeg;base64,',
    'data:image/jpg;base64,',
    'data:image/gif;base64,',
    'data:image/webp;base64,',
    'data:image/bmp;base64,',
];

const colourNotation = /^#[0-9a-fA-F]{6}$/;

const emphasisFlags = ['Bold', 'Italic', 'Underline', 'Strikethrough', 'Monospace'] as const;

const noEmphasis: MailTextEmphasis = {
    bold: false,
    italic: false,
    underline: false,
    strikethrough: false,
    monospace: false,
};

/**
 * Reads one message's body, answering an expected failure as a value rather than by throwing.
 *
 * `remoteImages` is the reader's own act on this one message, told to the service as a query and remembered by
 * neither side: opening the message again asks again. It is also what the parser holds a remote picture against, so a
 * document carrying an address nobody asked for is refused rather than drawn.
 */
export async function readMailBody(
    session: ClientSession,
    transport: MailFathomTransport,
    storedEmailId: string,
    remoteImages: boolean,
): Promise<ClientResult<MailBody>> {
    const response = await send(transport, {
        method: 'GET',
        path: routeFor(session, mailBodyRoute(storedEmailId, remoteImages)),
        headers: headersFor(session),
        longestAnswer: longestBodyAnswer,
    });

    if (response === null) {
        return failed('unavailable', null);
    }

    if (response.status !== 200) {
        return failed(failureReasonForStatus(response.status), response.status);
    }

    const body = parseBody(response.body, remoteImages);

    return body === null ? failed('unreadable', response.status) : read(body);
}

// How much of the document's own budget the walk has left. One object threaded through the walk rather than counters
// returned from it, because every bound here is about the document as a whole and a per-branch count would let a wide
// tree spend each of them once per branch.
interface RemainingBudget {
    blocks: number;
    inlineImageOctets: number;
}

function parseBody(body: string, remoteImages: boolean): MailBody | null {
    const record = asRecord(parsed(body));
    if (record === null) {
        return null;
    }

    const storedEmailId = record['storedEmailId'];
    const availability = record['availability'];
    const remoteImagesRequested = record['remoteImagesRequested'];

    if (typeof storedEmailId !== 'string' || typeof remoteImagesRequested !== 'boolean') {
        return null;
    }

    if (!isOneOf(availability, availabilities)) {
        return null;
    }

    const plainText = parseText(record['plainText']);
    if (plainText === null) {
        return null;
    }

    const carried = record['document'] ?? null;
    if (carried !== null && !isRecord(carried)) {
        return null;
    }

    const document = carried === null ? null : parseDocument(carried, remoteImagesRequested);
    if (carried !== null && document === null) {
        return null;
    }

    // The query the read was made with and the answer's own account of it have to agree, because everything the parser
    // admits about a remote picture is decided by that one boolean. A disagreement is a document composed for another
    // request, and reading it would be reading a picture reference against a permission nobody granted.
    if (remoteImagesRequested !== remoteImages) {
        return null;
    }

    return { storedEmailId, availability, plainText, document, remoteImagesRequested };
}

function parseText(value: unknown): MailBodyText | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const text = record['text'];
    const originalCharacterCount = record['originalCharacterCount'];
    const truncation = record['truncation'];

    if (typeof text !== 'string' || !isCount(originalCharacterCount) || !isOneOf(truncation, truncations)) {
        return null;
    }

    return { text, originalCharacterCount, truncation };
}

function parseDocument(record: Readonly<Record<string, unknown>>, remoteImages: boolean): MailDocument | null {
    const schemaVersion = record['schemaVersion'];
    const refusal = record['refusal'];
    const truncated = record['truncated'];
    const entries = record['blocks'];

    if (schemaVersion !== documentSchemaVersion || !isOneOf(refusal, refusals) || typeof truncated !== 'boolean') {
        return null;
    }

    const removedRemoteReferenceCount = record['removedRemoteReferenceCount'];
    const retainedRemoteImageCount = record['retainedRemoteImageCount'];
    const inlineImageCount = record['inlineImageCount'];
    const undrawnInlineImageCount = record['undrawnInlineImageCount'];

    if (
        !isCount(removedRemoteReferenceCount) ||
        !isCount(retainedRemoteImageCount) ||
        !isCount(inlineImageCount) ||
        !isCount(undrawnInlineImageCount) ||
        !Array.isArray(entries)
    ) {
        return null;
    }

    const budget: RemainingBudget = {
        blocks: bounds.maximumBlocks,
        inlineImageOctets: bounds.maximumInlineImageOctetsPerDocument,
    };

    const blocks = parseBlocks(entries, { remoteImages, depth: 1, budget });
    if (blocks === null) {
        return null;
    }

    return {
        blocks,
        refusal,
        removedRemoteReferenceCount,
        retainedRemoteImageCount,
        inlineImageCount,
        undrawnInlineImageCount,
        truncated,
    };
}

/** What a walk of one branch needs to know: what the reader asked for, how deep it already is, and what is left. */
interface Walk {
    readonly remoteImages: boolean;
    readonly depth: number;
    readonly budget: RemainingBudget;
}

function parseBlocks(entries: readonly unknown[], walk: Walk): MailDocumentBlock[] | null {
    if (walk.depth > bounds.maximumDepth) {
        return null;
    }

    const blocks: MailDocumentBlock[] = [];
    for (const entry of entries) {
        if (walk.budget.blocks <= 0) {
            return null;
        }

        walk.budget.blocks -= 1;

        const block = parseBlock(entry, walk);
        if (block === null) {
            return null;
        }

        blocks.push(block);
    }

    return blocks;
}

function parseBlock(value: unknown, walk: Walk): MailDocumentBlock | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const identity = record['type'];
    const version = record['version'];

    if (typeof identity !== 'string' || !isCount(version)) {
        return null;
    }

    // An identity nobody declares, and a declared identity at a revision this build does not implement, are the same
    // fact about the deployment on the other end of the connection: it is ahead of this client. Neither is a document
    // to refuse, because refusing would cost the reader a message over one block it could have been told about.
    if (blockRevisions[identity] !== version) {
        return { type: 'unimplemented', identity, version };
    }

    return parseCataloguedBlock(identity, record, walk);
}

function parseCataloguedBlock(
    identity: string,
    record: Readonly<Record<string, unknown>>,
    walk: Walk,
): MailDocumentBlock | null {
    switch (identity) {
        case 'paragraph':
            return parseParagraph(record);
        case 'heading':
            return parseHeading(record);
        case 'list':
            return parseList(record, walk);
        case 'table':
            return parseTable(record, walk);
        case 'quote':
            return parseQuote(record, walk);
        case 'image':
            return parseImageBlock(record, walk);
        case 'separator':
            return { type: 'separator' };
        case 'preformatted':
            return parsePreformatted(record);
        default:
            return null;
    }
}

function parseParagraph(record: Readonly<Record<string, unknown>>): MailParagraphBlock | null {
    const alignment = record['alignment'];
    const content = parseRuns(record['content']);

    if (content === null || !isOneOf(alignment, alignments)) {
        return null;
    }

    return { type: 'paragraph', content, alignment };
}

function parseHeading(record: Readonly<Record<string, unknown>>): MailHeadingBlock | null {
    const level = record['level'];
    const alignment = record['alignment'];
    const content = parseRuns(record['content']);

    if (content === null || !isOneOf(alignment, alignments) || !isCount(level) || level < 1 || level > 6) {
        return null;
    }

    return { type: 'heading', level, content, alignment };
}

function parseList(record: Readonly<Record<string, unknown>>, walk: Walk): MailListBlock | null {
    const ordered = record['ordered'];
    const entries = record['items'];

    // An item carrying no block charges nothing against the block budget, so the item count is bounded here as well:
    // the service emits an item only where it reduced to at least one block, which is what makes its own item count
    // transitively bounded by `MaximumBlocks`.
    if (typeof ordered !== 'boolean' || !Array.isArray(entries) || entries.length > bounds.maximumBlocks) {
        return null;
    }

    const items: MailListItem[] = [];
    for (const entry of entries) {
        const item = asRecord(entry);
        const blocks =
            item === null || !Array.isArray(item['blocks']) ? null : parseBlocks(item['blocks'], descend(walk));

        if (blocks === null) {
            return null;
        }

        items.push({ blocks });
    }

    return { type: 'list', ordered, items };
}

function parseTable(record: Readonly<Record<string, unknown>>, walk: Walk): MailTableBlock | null {
    const declared = record['columns'];
    const written = record['rows'];

    // A column is bounded by the same number a row's cells are, because that is what a column is: the service composes
    // one per cell position, so a declaration longer than a row can be wide is a document it did not write.
    if (!Array.isArray(declared) || declared.length > bounds.maximumTableCells) {
        return null;
    }

    if (!Array.isArray(written) || written.length > bounds.maximumTableRows) {
        return null;
    }

    const columns: MailTableColumn[] = [];
    for (const entry of declared) {
        const column = asRecord(entry);
        const widthShare = column?.['widthShare'] ?? null;

        if (column === null || (widthShare !== null && !isShare(widthShare))) {
            return null;
        }

        columns.push({ widthShare });
    }

    const rows: MailTableRow[] = [];
    for (const entry of written) {
        const row = parseTableRow(entry, walk);
        if (row === null) {
            return null;
        }

        rows.push(row);
    }

    return { type: 'table', columns, rows };
}

function parseTableRow(value: unknown, walk: Walk): MailTableRow | null {
    const record = asRecord(value);
    const isHeader = record?.['isHeader'];
    const written = record?.['cells'];

    if (typeof isHeader !== 'boolean' || !Array.isArray(written) || written.length > bounds.maximumTableCells) {
        return null;
    }

    const cells: MailTableCell[] = [];
    for (const entry of written) {
        const cell = parseTableCell(entry, walk);
        if (cell === null) {
            return null;
        }

        cells.push(cell);
    }

    return { isHeader, cells };
}

function parseTableCell(value: unknown, walk: Walk): MailTableCell | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const columnSpan = record['columnSpan'];
    const rowSpan = record['rowSpan'];
    const alignment = record['alignment'];
    const background = parseColour(record['background']);

    // Spans multiply, which is why the service clamps each one rather than admitting whatever the message declared: a
    // row of the permitted number of cells each claiming the permitted span is that number squared, out of markup a
    // message writes in a kilobyte.
    if (!isCount(columnSpan) || columnSpan < 1 || columnSpan > bounds.maximumTableCells) {
        return null;
    }

    if (!isCount(rowSpan) || rowSpan < 1 || rowSpan > bounds.maximumTableRows) {
        return null;
    }

    if (!isOneOf(alignment, alignments) || background === undefined || !Array.isArray(record['blocks'])) {
        return null;
    }

    const blocks = parseBlocks(record['blocks'], descend(walk));

    return blocks === null ? null : { columnSpan, rowSpan, alignment, background, blocks };
}

function parseQuote(record: Readonly<Record<string, unknown>>, walk: Walk): MailQuoteBlock | null {
    const depth = record['depth'];

    if (!isCount(depth) || depth < 1 || !Array.isArray(record['blocks'])) {
        return null;
    }

    const blocks = parseBlocks(record['blocks'], descend(walk));

    return blocks === null ? null : { type: 'quote', depth, blocks };
}

function parseImageBlock(record: Readonly<Record<string, unknown>>, walk: Walk): MailImageBlock | null {
    const alignment = record['alignment'];
    const image = parseImage(record['image'], walk);
    const link = parseLink(record['link'] ?? null);

    if (image === null || link === undefined || !isOneOf(alignment, alignments)) {
        return null;
    }

    return { type: 'image', image, link, alignment };
}

function parsePreformatted(record: Readonly<Record<string, unknown>>): MailPreformattedBlock | null {
    const text = record['text'];

    return typeof text === 'string' && text.length <= bounds.maximumCharactersPerRun
        ? { type: 'preformatted', text }
        : null;
}

function parseRuns(value: unknown): MailInlineRun[] | null {
    if (!Array.isArray(value) || value.length > bounds.maximumRunsPerBlock) {
        return null;
    }

    const runs: MailInlineRun[] = [];
    for (const entry of value) {
        const run = parseRun(entry);
        if (run === null) {
            return null;
        }

        runs.push(run);
    }

    return runs;
}

function parseRun(value: unknown): MailInlineRun | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const text = record['text'];
    const emphasis = parseEmphasis(record['emphasis']);
    const foreground = parseColour(record['foreground']);
    const link = parseLink(record['link'] ?? null);

    if (typeof text !== 'string' || text.length > bounds.maximumCharactersPerRun) {
        return null;
    }

    return emphasis === null || foreground === undefined || link === undefined
        ? null
        : { text, emphasis, foreground, link };
}

function parseEmphasis(value: unknown): MailTextEmphasis | null {
    if (typeof value !== 'string') {
        return null;
    }

    if (value === 'None') {
        return noEmphasis;
    }

    // The wire writes a set of flags as their names joined by a comma, which is what the serializer produces for the
    // enumeration. A name outside the set is a document this deployment did not compose.
    const named = value.split(',').map((flag) => flag.trim());
    if (named.some((flag) => !isOneOf(flag, emphasisFlags))) {
        return null;
    }

    return {
        bold: named.includes('Bold'),
        italic: named.includes('Italic'),
        underline: named.includes('Underline'),
        strikethrough: named.includes('Strikethrough'),
        monospace: named.includes('Monospace'),
    };
}

// `undefined` separates "absent, which is permitted" from "present and refused", which `null` alone cannot: a colour
// the message asked for and a colour it did not are both values the caller keeps.
function parseColour(value: unknown): string | null | undefined {
    if (value === null || value === undefined) {
        return null;
    }

    return typeof value === 'string' && colourNotation.test(value) ? value : undefined;
}

function parseLink(value: unknown): MailDocumentLink | null | undefined {
    if (value === null) {
        return null;
    }

    const record = asRecord(value);
    if (record === null) {
        return undefined;
    }

    const target = record['target'];
    const host = record['host'] ?? null;
    const asciiHost = record['asciiHost'] ?? null;
    const deception = record['deception'];
    const worthWarningAbout = record['isWorthWarningAbout'];

    if (typeof target !== 'string' || target.length > bounds.maximumLinkTargetLength || !isFollowable(target)) {
        return undefined;
    }

    if (!isOneOf(deception, deceptions) || typeof worthWarningAbout !== 'boolean') {
        return undefined;
    }

    if ((host !== null && typeof host !== 'string') || (asciiHost !== null && typeof asciiHost !== 'string')) {
        return undefined;
    }

    return { target, host, asciiHost, deception, worthWarningAbout };
}

function parseImage(value: unknown, walk: Walk): MailInlineImage | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const source = record['source'];
    const alternativeText = record['alternativeText'] ?? null;
    const width = record['width'] ?? null;
    const height = record['height'] ?? null;

    if (typeof source !== 'string' || !isDrawableSource(source, walk)) {
        return null;
    }

    if (
        alternativeText !== null &&
        (typeof alternativeText !== 'string' || alternativeText.length > bounds.maximumAlternativeTextLength)
    ) {
        return null;
    }

    // A dimension is drawn onto the element as the message declared it, so it is bounded the way the service bounds it
    // rather than left to any safe integer: one picture claiming a height of nine quadrillion pixels is a box nothing
    // scrolls past to reach the rest of the message.
    if (!isDrawableEdge(width) || !isDrawableEdge(height)) {
        return null;
    }

    return { source, alternativeText, width, height };
}

function isDrawableEdge(value: unknown): value is number | null {
    return value === null || (isCount(value) && value > 0 && value <= bounds.maximumPictureEdge);
}

/**
 * Answers whether a picture's source is one this pane draws, and charges what it costs against the document's budget.
 *
 * A remote address is admitted only for the read the reader asked pictures for, which is the client's half of the
 * property the service holds by removing every other one: a document carrying an address nobody asked for is a
 * document this deployment did not compose for this request.
 */
function isDrawableSource(source: string, walk: Walk): boolean {
    if (walk.remoteImages && /^https?:\/\//i.test(source)) {
        return source.length <= bounds.maximumLinkTargetLength;
    }

    // The service admits a media type case-insensitively and then writes the message's own spelling into the document,
    // so a part declaring `IMAGE/PNG` composes a source this would otherwise refuse — and refusing one costs the reader
    // the whole message rather than the picture.
    const lowered = source.toLowerCase();
    const prefix = drawableImagePrefixes.find((candidate) => lowered.startsWith(candidate));
    if (prefix === undefined) {
        return false;
    }

    // Base64 carries three octets in four characters, so the bound on the message's picture is read off the encoding
    // it arrived in rather than off a decode nothing needs to run.
    const octets = Math.floor(((source.length - prefix.length) * 3) / 4);
    if (octets === 0 || octets > bounds.maximumInlineImageOctets || octets > walk.budget.inlineImageOctets) {
        return false;
    }

    walk.budget.inlineImageOctets -= octets;

    return true;
}

function isFollowable(target: string): boolean {
    const scheme = target.toLowerCase();

    return followableSchemes.some((candidate) => scheme.startsWith(candidate));
}

function descend(walk: Walk): Walk {
    return { remoteImages: walk.remoteImages, depth: walk.depth + 1, budget: walk.budget };
}

function parsed(body: string): unknown {
    try {
        return JSON.parse(body);
    } catch {
        return null;
    }
}

function isCount(value: unknown): value is number {
    return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
}

function isShare(value: unknown): value is number {
    return typeof value === 'number' && Number.isFinite(value) && value >= 0 && value <= 1;
}

function isOneOf<TValue extends string>(value: unknown, admitted: readonly TValue[]): value is TValue {
    return typeof value === 'string' && admitted.includes(value as TValue);
}
