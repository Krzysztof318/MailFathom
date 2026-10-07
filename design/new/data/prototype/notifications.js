// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

MailFathomDesign.data.notifications = [
  { id: "n1", kind: "mail", unread: true, mins: 3, title: "Anna Kowalska replied", body: "CPI cap counter-proposal — she asks for approval by tomorrow.", src: "Mail · Contoso", go: { screen: "mail", thread: "contoso" } },
  { id: "n2", kind: "cal", unread: true, mins: 26, title: "Meeting in 30 minutes", body: "Contoso addendum review · Nordwind room and Teams.", src: "Calendar", go: { screen: "cal" } },
  { id: "n3", kind: "case", unread: true, mins: 74, title: "Fabrikam is waiting for confirmation", body: "The delivery confirmation is due 02.09.", src: "Cases · Fabrikam Q4 deliveries", go: { screen: "cases" } },
  { id: "n4", kind: "task", unread: false, mins: 150, title: "Task: IT contract list", body: "Audit is waiting for the list with notice periods.", src: "Tasks", go: { screen: "tasks" } },
  { id: "n5", kind: "mail", unread: false, mins: 320, title: "New attachment in the audit thread", body: "Marek Wiśniewski added two files to “IT contract audit”.", src: "Mail · Audit", go: { screen: "mail", thread: "audyt" } },
  { id: "n6", kind: "system", unread: false, mins: 700, title: "Sync finished", body: "3 accounts, 12 new threads, 1 account needs to sign in again.", src: "System", go: null },
  { id: "n7", kind: "cal", unread: false, mins: 1580, title: "Meeting invitation", body: "Nordwind — SLA negotiations, Tuesday 10:00.", src: "Calendar", go: { screen: "cal" } },
];

MailFathomDesign.data.incomingNotifications = [
  { kind: "mail", title: "Tomasz Zieliński: date confirmation", body: "Fabrikam confirms delivery on 12.11 — asks for approval.", src: "Mail · Fabrikam", go: { screen: "mail", thread: "fabrikam" } },
  { kind: "cal", title: "Meeting in 10 minutes", body: "Contoso — call about the CPI cap.", src: "Calendar", go: { screen: "cal" } },
  { kind: "case", title: "New document in a case", body: "Addendum no. 3 (version 4) attached to the Contoso 2027 case.", src: "Cases · Contoso renegotiation", go: { screen: "cases" } },
];

/* Demo refresh — one click plays a small, realistic inbox event: a mail lands, an existing
   thread gets a reply, and one message is withdrawn. Only the affected tiles animate; the
   list itself is never reloaded. */
MailFathomDesign.data.demoBatches = [
  {
    mail: { from: "Marta Wilk", org: "Nordwind", time: "now", subject: "Framework agreement — annex for signature",
      ai: "Awaiting our comments · deadline 15.09",
      meta: "Marta Wilk <m.wilk@nordwind.example> · today, now · thread: 1 message",
      state: [ { label: "AGREED", value: "Annex scope: support hours" }, { label: "OPEN QUESTION", value: "Penalties for exceeding response time" }, { label: "DEADLINE", value: "Comments by 15.09" } ],
      body: [ { text: "Attached is the annex to the framework agreement in the version after our call. The change concerns support hours (8–18 instead of 9–17) and the reporting procedure." },
        { text: "Please send comments by 15 September — after that date we send the document for signature." } ],
      attachments: [ { type: "PDF", name: "Annex_framework_agreement.pdf", size: "184 kB" } ] },
    notif: "Annex after the call — comments expected by 15.09.",
    edit: { id: "finanse", patch: { time: "now", subject: "Invoice 08/2026 — correction", ai: "Corrected amount €1,388 · new due date 05.09",
      meta: "invoices@company.example · today, now · thread: 2 messages" } },
    editNotif: { title: "Finance: invoice correction", body: "Corrected 08/2026 — €1,388 net, payment by 05.09.", thread: "finanse" },
    fallbackRemove: "jacek",
    removeNote: "The sender withdrew a message — the thread has left the list",
  },
  {
    mail: { from: "Tomasz Zieliński", org: "Fabrikam", time: "now", subject: "Delivery 12.11 — confirmation needed",
      ai: "Reply expected today · date confirmation",
      meta: "t.zielinski@fabrikam.example · today, now · thread: 1 message",
      state: [ { label: "AGREED", value: "Delivery window 12.11, morning" }, { label: "OPEN QUESTION", value: "Who receives on our side" }, { label: "COMMITMENT", value: "Confirmation today" } ],
      body: [ { text: "We confirm the delivery on 12.11 in the morning window. We need the name of the person receiving and a contact number for the driver." },
        { text: "Without confirmation today the slot goes to the next order in the queue." } ],
      attachments: [] },
    notif: "Fabrikam confirms 12.11 — asks for the receiving person today.",
    edit: { id: "marta", patch: { time: "now", subject: "Re: CPI calculation 2027 — cap accepted", ai: "Finance accepts a 5% cap · ready for the annex",
      meta: "m.nowak@company.example · today, now · thread: 5 messages" } },
    editNotif: { title: "Marta Nowak replied", body: "Finance accepts a 5% cap on indexation.", thread: "marta" },
    fallbackRemove: "jacek",
    removeNote: "The sender withdrew a message — the thread has left the list",
  },
  {
    mail: { from: "Karolina Bąk", org: "Contoso", time: "now", subject: "Signatory for the addendum — details",
      ai: "Data complete · nothing to do on our side",
      meta: "k.bak@contoso.example · today, now · thread: 1 message",
      state: [ { label: "AGREED", value: "Signatory: Anna Kowalska, board member" }, { label: "OPEN QUESTION", value: "None" }, { label: "COMMITMENT", value: "eSign link tomorrow" } ],
      body: [ { text: "Passing on the signatory details for addendum no. 3: Anna Kowalska, board member, acting under the entry in the register." },
        { text: "The eSign link goes out tomorrow morning — nothing more is needed from your side." } ],
      attachments: [] },
    notif: "Contoso passed the signatory details for addendum no. 3.",
    edit: { id: "contoso", patch: { time: "now", subject: "Contract addendum — CPI cap agreed", ai: "Cap 5% accepted · ready for signature",
      meta: "Anna Kowalska <a.kowalska@contoso.example> · today, now · thread: 7 messages" } },
    editNotif: { title: "Anna Kowalska replied", body: "Contoso accepts a 5% cap — the addendum goes to signature.", thread: "contoso" },
    fallbackRemove: "jacek",
    removeNote: "The sender withdrew a message — the thread has left the list",
  },
];
