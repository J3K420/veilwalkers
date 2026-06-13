export const meta = {
  name: 'story-cr-fanout',
  description: 'Adversarial 3-layer code review of a story diff (Blind Hunter / Edge Case Hunter / Acceptance Auditor) with verify-disputed-first triage',
  whenToUse: 'The CR phase of the Veilwalkers story-automator. Pass {diffPath, storyPath, slug} via args. Produces a triaged findings report; the orchestrator applies patches and records deferrals.',
  phases: [
    { title: 'Review', detail: 'three adversarial layers read the same diff in parallel' },
    { title: 'Verify', detail: 'independently re-confirm each disputed/contested finding before triage' },
  ],
}

// --- args contract -----------------------------------------------------------
// args = {
//   diffPath:  abs path to the range-diff temp file (baseline..HEAD), already written by the orchestrator
//   storyPath: abs path to the story .md (ACs + Dev Notes + sanctioned-deviation matrix)
//   slug:      story slug, e.g. "1-6-configure-economy-values-data-drivenly" (labels only)
//   archPath:  abs path to docs/architecture.md
//   epicsPath: abs path to docs/epics.md
// }
// The orchestrator MUST have written diffPath before calling this workflow and
// MUST delete it before committing (per bmad-code-review-pattern memory).
// Accept args as a real object (correct usage) OR a JSON string (defensive: a
// stringified args payload is an easy caller mistake and would otherwise surface
// as every field being undefined).
let parsedArgs = args || {}
if (typeof parsedArgs === 'string') {
  try { parsedArgs = JSON.parse(parsedArgs) } catch (e) { parsedArgs = {} }
}
const { diffPath, storyPath, slug, archPath, epicsPath } = parsedArgs
if (!diffPath || !storyPath || !slug) {
  throw new Error('story-cr-fanout requires args {diffPath, storyPath, slug, archPath, epicsPath}')
}

const FINDINGS_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  required: ['layer', 'findings'],
  properties: {
    layer: { type: 'string' },
    findings: {
      type: 'array',
      items: {
        type: 'object',
        additionalProperties: false,
        required: ['title', 'file', 'line', 'severity', 'detail', 'isAcViolation'],
        properties: {
          title: { type: 'string', description: 'one-line summary of the issue' },
          file: { type: 'string', description: 'repo-relative path, or "" if cross-cutting' },
          line: { type: 'string', description: 'line number or range as a string, or "" if N/A' },
          severity: { type: 'string', enum: ['blocker', 'major', 'minor', 'nit'] },
          detail: { type: 'string', description: 'what is wrong and why it matters; concrete, not vague' },
          isAcViolation: { type: 'boolean', description: 'true only if it violates a stated acceptance criterion' },
        },
      },
    },
  },
}

const VERDICT_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  required: ['title', 'file', 'verdict', 'reason', 'recommendedAction'],
  properties: {
    title: { type: 'string' },
    file: { type: 'string' },
    verdict: {
      type: 'string',
      enum: ['confirmed', 'spec-sanctioned', 'false-positive', 'out-of-scope'],
      description: 'confirmed = a real defect in scope; spec-sanctioned = the diff matches an explicit Dev-Notes/architecture allowance; false-positive = the reviewer misread; out-of-scope = real but belongs to a later story',
    },
    reason: { type: 'string', description: 'cite the AC, Dev-Notes deviation, or code that decides the verdict' },
    recommendedAction: { type: 'string', enum: ['patch', 'defer', 'drop'] },
  },
}

phase('Review')

// Three layers, same diff, in parallel — matched to the bmad-code-review-pattern memory.
//  - Blind Hunter: diff ONLY, reads nothing else (catches what the spec would rationalize away).
//  - Edge Case Hunter: diff + repo read access (boundary/branch coverage).
//  - Acceptance Auditor: diff + story + architecture + epics (AC conformance + Dev Agent Record vs diff).
const BLIND_PROMPT = `You are the BLIND HUNTER in an adversarial code review.
Read ONLY this diff file: ${diffPath}. Do NOT open any other file, the story, or the architecture — you are deliberately spec-blind so you catch issues a spec would rationalize away.
Hunt for: correctness bugs, unhandled throws, broken invariants, rollback/atomicity gaps, race windows, resource leaks, contradictory or dead code, doc-comments that contradict the code.
Report every concrete issue you find with file + line from the diff hunks. Mark isAcViolation=false for all of yours (you have no spec to judge ACs). Be specific — "this could be null" must say which value on which line and why.
Return the FINDINGS object (layer="blind-hunter").`

const EDGE_PROMPT = `You are the EDGE CASE HUNTER in an adversarial code review.
Primary input is the diff at ${diffPath}; you MAY read the surrounding repo files to confirm behavior at boundaries. Method-driven, not attitude-driven: walk every branching path and boundary condition the diff introduces or touches — zero/empty/negative inputs, off-by-one on thresholds and level/XP curves, enum-default branches, first-launch vs returning, exact-boundary spends, concurrent mutate-and-persist windows, ScriptableObject-not-assigned / null-asset paths, values that round-trip through the encrypted save.
For each unhandled or mishandled case, report file + line + the exact condition that breaks it. Return the FINDINGS object (layer="edge-case-hunter").`

const AUDIT_PROMPT = `You are the ACCEPTANCE AUDITOR in an adversarial code review.
Read the diff at ${diffPath}, the story at ${storyPath}, plus architecture at ${archPath} and epics at ${epicsPath}.
Two jobs: (1) verify EVERY acceptance criterion in the story is actually satisfied by the diff — mark genuine AC gaps isAcViolation=true; (2) check the Dev Agent Record (File List + Tasks/Subtasks + Implementation Plan) against what the diff actually changed — flag missing files, stale plan wording, or unchecked-but-done / checked-but-not-done items.
CRITICAL: before reporting any "contradiction," check the story's Dev Notes for a sanctioned-deviation matrix (this story explicitly sanctions placements like the atomicity test living in Economy.Tests, costs as injected parameters, etc.). A diff that matches a sanctioned deviation is NOT a finding. Return the FINDINGS object (layer="acceptance-auditor").`

const reviews = await parallel([
  () => agent(BLIND_PROMPT, { label: `review:blind:${slug}`, phase: 'Review', schema: FINDINGS_SCHEMA }),
  () => agent(EDGE_PROMPT, { label: `review:edge:${slug}`, phase: 'Review', schema: FINDINGS_SCHEMA, agentType: 'Explore' }),
  () => agent(AUDIT_PROMPT, { label: `review:audit:${slug}`, phase: 'Review', schema: FINDINGS_SCHEMA, agentType: 'Explore' }),
])

const layers = reviews.filter(Boolean)
const allFindings = layers.flatMap((r) => (r.findings || []).map((f) => ({ ...f, layer: r.layer })))

// Dedup signal: when two independent layers report the same file+title-ish issue, that is the
// strong "this is real" marker from the memory. Group by file+normalized-title; a finding seen by
// >=2 layers is pre-weighted "converged" and still goes through verification (verification is cheap
// insurance, and the Blind Hunter's spec-blindness means some converged items are still sanctioned).
const norm = (s) => (s || '').toLowerCase().replace(/[^a-z0-9]+/g, ' ').trim()
const byKey = new Map()
for (const f of allFindings) {
  const key = `${f.file}::${norm(f.title).slice(0, 60)}`
  if (!byKey.has(key)) byKey.set(key, { ...f, layers: [f.layer] })
  else {
    const e = byKey.get(key)
    if (!e.layers.includes(f.layer)) e.layers.push(f.layer)
    // keep the most severe wording
    const rank = { blocker: 3, major: 2, minor: 1, nit: 0 }
    if (rank[f.severity] > rank[e.severity]) { e.severity = f.severity; e.detail = f.detail }
    e.isAcViolation = e.isAcViolation || f.isAcViolation
  }
}
const deduped = [...byKey.values()]
log(`Review surfaced ${allFindings.length} raw findings → ${deduped.length} deduped (${deduped.filter((d) => d.layers.length > 1).length} converged across layers).`)

phase('Verify')

// Verify-disputed-first: every deduped finding gets an independent verifier that re-reads the diff +
// the story's Dev Notes and decides confirmed / spec-sanctioned / false-positive / out-of-scope.
// This is the "verify disputed findings myself before triage" gate from the memory, done per-finding
// and concurrently. AC-violation claims and converged findings are verified too — no finding is
// trusted on the strength of one layer's word.
const verdicts = await parallel(
  deduped.map((f) => () =>
    agent(
      `Independently verify this code-review finding before it is triaged. Re-read the diff at ${diffPath} and the relevant section of the story at ${storyPath} (especially its Dev Notes sanctioned-deviation matrix and the cited acceptance criteria).

FINDING (from layer(s) ${f.layers.join('+')}, severity ${f.severity}${f.isAcViolation ? ', claimed AC violation' : ''}):
  title: ${f.title}
  file:  ${f.file}  line: ${f.line}
  detail: ${f.detail}

Decide the verdict:
- "confirmed": a real defect, in scope for THIS story, that should change before the story is done.
- "spec-sanctioned": the diff matches an explicit allowance in the story's Dev Notes / architecture (cite it). NOT a defect.
- "false-positive": the reviewer misread the code; show why it is actually fine.
- "out-of-scope": real, but it belongs to a later story / a deferred-work item, not this one.

Default toward "spec-sanctioned" or "out-of-scope" only when you can cite the sanctioning text; otherwise, if it is a genuine in-scope defect, say "confirmed". Return the VERDICT object.`,
      { label: `verify:${f.file || 'x-cutting'}`, phase: 'Verify', schema: VERDICT_SCHEMA, agentType: 'Explore' }
    ).then((v) => (v ? { ...f, ...v } : null))
  )
)

const triaged = verdicts.filter(Boolean)
const confirmed = triaged.filter((v) => v.verdict === 'confirmed')
const sanctioned = triaged.filter((v) => v.verdict === 'spec-sanctioned')
const falsePos = triaged.filter((v) => v.verdict === 'false-positive')
const outOfScope = triaged.filter((v) => v.verdict === 'out-of-scope')

const patches = confirmed.filter((v) => v.recommendedAction === 'patch')
const defers = [...confirmed.filter((v) => v.recommendedAction === 'defer'), ...outOfScope]

log(`Triage: ${patches.length} to patch, ${defers.length} to defer, ${sanctioned.length} spec-sanctioned, ${falsePos.length} false-positive.`)

// Return the full triage so the orchestrator (main loop) applies patches, re-runs the headless
// test gate, writes ### Review Findings into the story, and appends deferrals to deferred-work.md.
return {
  slug,
  diffPath,
  counts: {
    raw: allFindings.length,
    deduped: deduped.length,
    confirmed: confirmed.length,
    patch: patches.length,
    defer: defers.length,
    sanctioned: sanctioned.length,
    falsePositive: falsePos.length,
  },
  patches,
  defers,
  sanctioned,
  falsePositives: falsePos,
  acViolations: triaged.filter((v) => v.isAcViolation && v.verdict === 'confirmed'),
}
