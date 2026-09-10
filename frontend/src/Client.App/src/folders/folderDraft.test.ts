// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { describe, expect, it } from 'vitest';
import {
    draftAlias,
    draftForEditedFolder,
    draftForNewFolder,
    draftRemotePath,
    refusalOf,
    remotePathBase,
    withName,
    withRemotePath,
    type FolderDraft,
} from './folderDraft';

const inMailbox = draftForNewFolder('work', 'Work', null);

const insideProjects = draftForNewFolder('work', 'Work', { alias: 'PROJECTS', remotePath: ['INBOX', 'Projects'] });

function named(draft: FolderDraft, name: string): FolderDraft {
    return withName(draft, name);
}

describe('withName', () => {
    it('proposes a path under the root of the mailbox for a folder made at the top of one', () => {
        expect(named(inMailbox, 'Contracts').remotePath).toBe('INBOX/Contracts');
    });

    it('proposes a path under the parent’s own place on the server for a folder made inside one', () => {
        expect(named(insideProjects, '2027').remotePath).toBe('INBOX/Projects/2027');
    });

    it('empties the proposal again when the name is emptied, rather than leaving a path to nowhere', () => {
        expect(named(named(inMailbox, 'Contracts'), '').remotePath).toBe('');
    });

    it('stops proposing once the path has been typed in, so a correction to the name does not move the mail', () => {
        const written = withRemotePath(named(inMailbox, 'Contracts'), 'Archive/Contracts');

        expect(named(written, 'Contracts 2027').remotePath).toBe('Archive/Contracts');
    });
});

describe('draftAlias', () => {
    it('names a folder made at the top of a mailbox by its own name', () => {
        expect(draftAlias(named(inMailbox, 'Contracts'))).toBe('Contracts');
    });

    it('names one made inside a folder by its parent’s alias and its own name', () => {
        expect(draftAlias(named(insideProjects, '2027'))).toBe('PROJECTS/2027');
    });

    it('reads a separator typed into a name as a space, because a name is one level of the tree', () => {
        expect(draftAlias(named(inMailbox, 'Q1/Q2'))).toBe('Q1 Q2');
    });
});

describe('draftRemotePath', () => {
    it('answers the levels the path field states, outermost first', () => {
        expect(draftRemotePath(named(insideProjects, '2027'))).toEqual(['INBOX', 'Projects', '2027']);
    });

    it('drops the empty levels a trailing or doubled separator leaves', () => {
        expect(draftRemotePath(withRemotePath(inMailbox, 'INBOX//Contracts/'))).toEqual(['INBOX', 'Contracts']);
    });
});

describe('remotePathBase', () => {
    it('answers where the folder would go, which is the parent’s own place on the server', () => {
        expect(remotePathBase(insideProjects)).toBe('INBOX/Projects/');
    });

    it('answers the root of the mailbox for a folder made at the top of one', () => {
        expect(remotePathBase(inMailbox)).toBe('INBOX/');
    });
});

describe('draftForEditedFolder', () => {
    it('opens on the folder as it stands, with the path already the person’s so a rename cannot move it', () => {
        const draft = draftForEditedFolder(
            'work',
            'Work',
            { alias: 'PROJECTS/2026', remotePath: ['INBOX', 'Projects', '2026'] },
            { alias: 'PROJECTS', remotePath: ['INBOX', 'Projects'] },
        );

        expect(draft.mode).toBe('edit');
        expect(draft.standingAlias).toBe('PROJECTS/2026');
        expect(draft.name).toBe('2026');
        expect(draft.remotePath).toBe('INBOX/Projects/2026');
        expect(named(draft, '2027').remotePath).toBe('INBOX/Projects/2026');
    });
});

describe('refusalOf', () => {
    it('lets a named folder with a path through', () => {
        expect(refusalOf(named(inMailbox, 'Contracts'), [])).toBeNull();
    });

    it('refuses a folder with no name, which is what the design draws the button flat for', () => {
        expect(refusalOf(inMailbox, [])).toBe('nameEmpty');
    });

    it('refuses a name that is nothing but spaces, which is a name nobody could point at', () => {
        expect(refusalOf(named(inMailbox, '   '), [])).toBe('nameEmpty');
    });

    it('refuses a folder that would nest past the third level of an alias', () => {
        const deep = draftForNewFolder('work', 'Work', { alias: 'A/B/C', remotePath: ['A', 'B', 'C'] });

        expect(refusalOf(named(deep, 'D'), [])).toBe('tooDeep');
    });

    it('refuses a path the person emptied by hand, there being nowhere on the server to read', () => {
        expect(refusalOf(withRemotePath(named(inMailbox, 'Contracts'), '  '), [])).toBe('remotePathEmpty');
    });

    it('refuses a name the mailbox already declares, before the deployment has to say so', () => {
        expect(refusalOf(named(inMailbox, 'Contracts'), ['INBOX', 'CONTRACTS'])).toBe('aliasTaken');
    });

    it('lets an edit keep its own alias, which is the one collision that is not one', () => {
        const draft = draftForEditedFolder('work', 'Work', { alias: 'CONTRACTS', remotePath: ['Contracts'] }, null);

        expect(refusalOf(draft, ['INBOX', 'CONTRACTS'])).toBeNull();
    });
});
