Closes #

## What changes

<!-- What this does and why this shape. The issue holds the requirement; this says how it was met and
     what a reviewer should look at first. Name anything you decided rather than derived. -->

## How it was verified

<!-- `scripts/verify-full.sh` output, which tests were added, and anything checked by hand. Say what
     you could not verify and why. -->

## Checklist

- [ ] `bash scripts/verify-full.sh` passes on this branch, rebased onto current `main`.
- [ ] Behavior changes are covered by unit tests, and the coverage gate passes.
- [ ] Affected documentation is updated in this change set.
- [ ] `bash scripts/review-obligations.sh` was run, and every row it reported is answered — by a test, by a page, or by why nothing is owed there.
- [ ] No `Co-authored-by:` or other co-author trailer on any commit.
- [ ] `CHANGELOG.md` is untouched — it is written by the release pull request alone.
- [ ] Every new C# file carries the repository's standard header, applied by `scripts/verify-fast.sh` rather than typed, and no file gained a second copyright line, an author tag, a name, or a contact detail.
- [ ] Nothing here is under terms MailFathom could not distribute under Apache-2.0. A new dependency, service, container image, or externally sourced sample has its row in `THIRD_PARTY_LICENSES.md` in this change set.
- [ ] No credential, token, private key, real mailbox data, or personal information is in the diff.
- [ ] Opened as a draft, and marked ready for review only once the above holds.

<!-- First contribution? CONTRIBUTING.md covers all of the above, including how contributions are
     licensed — there is nothing to sign, and no acceptance comment to post. -->
