// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

MailFathomDesign.data.cases = [
  {
    id: "contoso27", title: "Contoso contract renegotiation 2027", parties: "Contoso · Nordwind",
    status: "Decision by 28.08", statusKind: "urgent", owner: "KK",
    summary: "Addendum no. 3 changes the SLA and the price. We accept the shorter response time and are negotiating an upper CPI cap.",
    scope: "4 threads · 3 documents · 2 accounts",
    agreed: [
      { text: "SLA for critical incidents: 2 h on business days", src: "SLA addendum.pdf" },
      { text: "Fee +8% from the 2027 period", src: "SLA addendum.pdf" },
      { text: "An indexation cap is contractually permissible", src: "Legal opinion — Wrona" },
    ],
    openQ: [
      { text: "Will Contoso accept a 5% CPI cap", src: "Thread: Contract addendum" },
      { text: "Who signs on our side", src: "Thread: Re: proposed terms" },
    ],
    dates: [
      { text: "Decision on the addendum", when: "28.08", src: "Thread: Contract addendum" },
      { text: "Reply to Marta about the calculation", when: "27.08", src: "Thread: CPI calculation" },
      { text: "New price takes effect", when: "01.01.2027", src: "SLA addendum.pdf" },
    ],
    facts: [
      { p: "Response time (critical)", a: "4 h", b: "2 h", src: "§ 1 of the addendum" },
      { p: "Annual fee", a: "€236,000", b: "€254,880", src: "§ 2 of the addendum" },
      { p: "CPI indexation cap", a: "none", b: "none — 5% under negotiation", src: "§ 3 of the addendum" },
      { p: "Notice period", a: "3 months", b: "unchanged", src: "Master agreement" },
    ],
    timeline: [
      { d: "14.06.2021", t: "Master agreement signed" },
      { d: "21.08.2026", t: "Outline of changes from Contoso" },
      { d: "25.08.2026", t: "Cost calculation (Marta)" },
      { d: "27.08.2026", t: "Legal opinion on the cap" },
      { d: "today", t: "Addendum in signature version" },
    ],
    threads: [
      { id: "contoso", label: "Contract addendum — signatures", meta: "Contoso · 6 messages" },
      { id: "anna2", label: "Re: proposed terms for 2027", meta: "Contoso · 3 messages" },
      { id: "marta", label: "CPI calculation 2027", meta: "Finance · 4 messages" },
      { id: "jacek", label: "Legal opinion — indexation cap", meta: "Law office · 2 messages" },
    ],
    docs: [
      { type: "PDF", name: "SLA addendum.pdf", size: "248 kB" },
      { type: "PDF", name: "Master agreement.pdf", size: "1.2 MB" },
      { type: "XLSX", name: "CPI_2027.xlsx", size: "34 kB" },
    ],
    duties: [
      { who: "Karolina", what: "Reply with a 5% cap counter-proposal", when: "28.08", state: "open" },
      { who: "Marta", what: "Update the calculation after the decision", when: "30.08", state: "waiting" },
      { who: "Contoso", what: "Addendum version with the cap", when: "02.09", state: "on their side" },
    ],
  },
  {
    id: "fabrikam", title: "Fabrikam Q4 deliveries", parties: "Fabrikam · Logistics",
    status: "Confirmation by 02.09", statusKind: "attention", owner: "KK",
    summary: "The manufacturer moved the delivery from 28.10 to 12.11. We need to confirm whether we accept the delay.",
    scope: "2 threads · 1 document",
    agreed: [{ text: "Commercial terms unchanged", src: "Thread: Q4 delivery dates" }],
    openQ: [{ text: "Do we accept the 15-day slip", src: "Thread: Q4 delivery dates" }],
    dates: [{ text: "Confirm the date", when: "02.09", src: "Thread: Q4 delivery dates" }],
    facts: [
      { p: "Delivery date", a: "28.10", b: "12.11", src: "Fabrikam message" },
      { p: "Price", a: "unchanged", b: "unchanged", src: "Fabrikam message" },
    ],
    timeline: [
      { d: "12.08.2026", t: "Order confirmed" },
      { d: "yesterday", t: "Notice of the line stoppage" },
    ],
    threads: [{ id: "fabrikam1", label: "Q4 delivery dates", meta: "Fabrikam · 7 messages" }],
    docs: [],
    duties: [{ who: "Karolina", what: "Decide whether to accept the date", when: "02.09", state: "open" }],
  },
  {
    id: "audyt", title: "IT contract audit 2024–2026", parties: "Internal Audit",
    status: "List due 03.09", statusKind: "attention", owner: "KK",
    summary: "Audit is asking for a list of IT contracts with addenda and notice periods.",
    scope: "2 threads · 1 document",
    agreed: [{ text: "Scope: IT contracts 2024–2026", src: "Thread: Contract register request" }],
    openQ: [{ text: "Who prepares the list", src: "Thread: Contract register request" }],
    dates: [{ text: "List submitted", when: "03.09", src: "Thread: Contract register request" }],
    facts: [{ p: "Contracts in scope", a: "—", b: "12 (preliminary)", src: "Contract register" }],
    timeline: [{ d: "yesterday", t: "Audit request" }],
    threads: [{ id: "audyt1", label: "Contract register request", meta: "Audit · 3 messages" }],
    docs: [{ type: "DOCX", name: "NDA_Northwind.docx", size: "72 kB" }],
    duties: [{ who: "Karolina", what: "IT contract list", when: "03.09", state: "open" }],
  },
];

// What a new case is filled in with: from a sentence (`counterparties` are recognised in it), or from the open thread.
MailFathomDesign.data.newCase = {
  counterparties: ["Contoso", "Fabrikam", "Adventure Works", "Northwind", "Audit"],
  ourSide: "Nordwind",
  due: "12.09",
  found: [
    { kind: "thread", label: "Question about weekend SLA — 4 messages" },
    { kind: "document", label: "Master agreement.pdf" },
    { kind: "deadline", label: "Reply by 01.09" },
  ],
  fromThread: { due: "28.08", deadline: "Decision by 28.08" },
};
