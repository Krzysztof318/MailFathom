# MailFathom Contributor Licence Agreement

**Version 1.0**

Thank you for contributing to MailFathom. This agreement records the terms under which your
contribution is accepted. It is a licence, not a transfer: **you keep the copyright in everything you
write.**

You accept it once, on your first pull request, by posting the sentence
[*Accepting the agreement*](#accepting-the-agreement) names. It then covers every contribution you make
to this repository, including the ones you have already submitted, until a new version of this document
replaces it.

## Why this exists

MailFathom is published under the [GNU Affero General Public License, Version 3](LICENSE), SPDX
identifier `AGPL-3.0-only`. That licence has no clause placing an inbound contribution under the
project's own terms — Apache-2.0, which MailFathom carried until 2026, had one in its section 5 and
the AGPL has no equivalent. So this agreement carries the first of the three things below rather than
merely adding to it, and clause 2.1 is what makes your contribution usable.

The second is the freedom to publish MailFathom under other terms later — a different open-source
licence, or a commercial licence sold beside it. Without this agreement a single merged contribution
would make that decision impossible to take without finding and asking every past contributor
individually. This agreement keeps the decision open, and the move from Apache-2.0 to AGPL-3.0-only is
the first time it was taken. It announces no further one.

The third is your statement that the code was yours to give. A licence says which terms a contribution
arrives under; it does not say that the person submitting it was entitled to submit it. Section 3 below
is where you say so.

## 1. Definitions

**"You"** means the individual accepting this agreement, or the legal entity on whose behalf it is
accepted. An entity and the individuals controlling, controlled by, or under common control with it are
one **"You"** for the purposes of this agreement.

**"Contribution"** means any work of authorship you intentionally submit for inclusion in MailFathom —
code, tests, documentation, configuration, deployment assets, or anything else — including every
modification to an existing work. Submission means any form of communication sent to the maintainers or
to this repository for the purpose of having the work included, whether by pull request, patch, issue, or
otherwise. Communication you conspicuously mark **"Not a Contribution"** is excluded.

**"MailFathom"** means the project hosted at <https://github.com/Krzysztof318/MailFathom> and the works
distributed from it.

**"Owner"** means Krzysztof Kasprowicz, a sole trader established in the Republic of Poland and trading
as **IMPE**, who is the copyright holder of record named in [`NOTICE`](NOTICE) and who publishes
MailFathom from the GitHub account [`Krzysztof318`](https://github.com/Krzysztof318); and any successor
the agreement is assigned to under clause 2.4. Where the Owner has to be identified beyond that — in a
transaction, or in a dispute — the business registration behind the trading name is what identifies
them, and it is supplied on request rather than published here.

## 2. Grants

**2.1 Copyright licence.** You grant the Owner a perpetual, worldwide, non-exclusive, irrevocable,
royalty-free copyright licence to reproduce, prepare derivative works of, publicly display, publicly
perform, sublicense, and distribute your Contribution and such derivative works, **under any licensing
terms the Owner chooses**, including terms that differ from the licence MailFathom carries when you
submit it and including proprietary terms.

This is the clause that carries your contribution into MailFathom and that keeps future licensing
decisions available. It is non-exclusive: your own rights in your Contribution are undiminished, and you
may use, license, and distribute it however you wish, to anyone, on any terms.

**2.2 Patent licence.** You grant the Owner and every recipient of MailFathom a perpetual, worldwide,
non-exclusive, irrevocable, royalty-free patent licence to make, have made, use, offer to sell, sell,
import, and otherwise transfer your Contribution, covering only those claims of your patents that are
necessarily infringed by your Contribution alone or by its combination with MailFathom. This grant
matches section 11 of the GNU Affero General Public License; it is stated here in its own right so it
survives a change of licence made under clause 2.1.

If you institute patent litigation against anyone alleging that MailFathom or a Contribution in it
constitutes patent infringement, the patent licences granted to you under this agreement terminate on the
day the action is filed.

**2.3 What is not granted.** No trademark rights are granted. Nothing here transfers ownership of your
Contribution, and nothing here obliges the Owner to use, merge, or distribute it.

**2.4 Who holds these rights.** The Owner may assign this agreement, and the rights granted under it,
to a successor in interest to MailFathom — a company the Owner forms or controls, or an acquirer of
the project — and the assignee then stands in the Owner's place. No further acceptance is asked of
you when that happens, which is the point of saying so here rather than discovering later that every
past contributor has to be found again.

An assignee takes the obligations with the rights: your Contribution stays yours, clause 2.1 stays
non-exclusive, and nothing an assignment does can enlarge what you granted.

## 3. What you state

By accepting this agreement you state that:

1. each Contribution is your original creation, or you have the right to submit it under this agreement;
2. you are legally entitled to grant the licences in clause 2, and no agreement with an employer or any
   other party prevents it — where your employer has rights in work you create, you have permission to
   contribute on their behalf, or your employer has waived those rights, or your employer has accepted
   this agreement;
3. every third-party work in a Contribution is identified in the pull request that carries it, together
   with the licence it arrives under and any restriction attached to it, and is compatible with the terms
   MailFathom is distributed under;
4. no Contribution contains a credential, token, private key, real mailbox content, or any identifiable
   personal information; and
5. you will tell the Owner if any of the above stops being true.

A Contribution you did not write, or did not write alone, may still be submitted — you mark it as such in
the pull request, name where it came from, and submit it separately from your own work.

## 4. No warranty and no obligation

A Contribution is provided **as is**, without warranty or condition of any kind, express or implied. You
are not obliged to provide support for a Contribution, and you may withdraw that support at any time
without notice. Nothing in this agreement requires the Owner to accept, review, or keep a Contribution.

## 5. Accepting the agreement

Accepting is one comment on your pull request, from the GitHub account that authored it:

```text
I have read the MailFathom Contributor Licence Agreement and I accept it.
```

Post that sentence as the whole of your comment, with nothing before it and nothing after. Casing,
surrounding whitespace, and a trailing full stop are ignored; anything else is not, because a sentence
quoted inside a question about whether to accept is not an acceptance and nothing reading the comment
afterwards could tell the two apart.

The `Contributor licence` workflow reads the comment, records the acceptance, and turns the
`license/cla` status green. What it records — your GitHub account and its numeric id, the pull request
and the comment that carried the acceptance, the version of this agreement, and the exact revision of
this file you accepted — lives on the `cla-signatures` branch of this repository, in the open, and is
not sent anywhere else. That record is the evidence the agreement exists; it holds no more of your data
than the pull request already does.

Once recorded, you never do this again. A later version of this agreement is a new acceptance, asked for
in the same way.

## 6. Governing terms

This agreement is governed by the law of the Republic of Poland, without regard to its conflict-of-law
rules. If a provision is held unenforceable, the rest stands.

---

This agreement is adapted from the [Apache Software Foundation Individual Contributor License
Agreement](https://www.apache.org/licenses/icla.pdf), which is published under the Apache License,
Version 2.0.
