# Universal Mobile Layout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every authenticated NexoMail section usable from 360 px while keeping Nexi search visible and presenting inbox rows as readable mobile cards.

**Architecture:** Add one final responsive layer loaded after existing feature styles so mobile layout has a single source of precedence. Preserve desktop markup and progressively rearrange existing semantic elements with CSS grid areas; use horizontal scrolling only for dense administrative tables.

**Tech Stack:** React, TypeScript, Vite, CSS media queries, pnpm, Playwright/Chromium visual checks when available.

**Spec:** `docs/superpowers/specs/2026-09-13-universal-mobile-layout-design.md`

## Global Constraints

- Minimum supported width: 360 px.
- Support light and dark themes.
- Preserve all desktop information and behavior.
- Do not change APIs or persisted data.

---

### Task 1: Responsive regression guard

**Files:**
- Create: `scripts/test-universal-mobile-layout.mjs`
- Modify: `package.json`

**Interfaces:**
- Consumes: compiled CSS source files and inbox markup class names.
- Produces: `pnpm test:mobile-layout` with nonzero exit on missing mobile contracts.

- [x] Write a test that requires a two-row mobile topbar, full-width `.top-search`, hidden `.message-list-header`, card-based `.message-row`, and controlled table overflow.
- [x] Run `pnpm test:mobile-layout` and confirm failure because the universal rules do not exist.
- [x] Register the test command without changing production behavior.

### Task 2: Universal mobile shell and sections

**Files:**
- Create: `src/frontend/src/styles/universal-mobile-layout.css`
- Modify: `src/frontend/src/main.tsx`

**Interfaces:**
- Consumes: existing `.topbar`, `.top-search`, `.main-content`, page, modal, toolbar, grid and table classes.
- Produces: final-cascade mobile rules for 360–767 px and compact tablet rules for 768–1023 px.

- [x] Add the stylesheet import last in `main.tsx`.
- [x] Implement the two-row topbar and full-width Nexi search.
- [x] Normalize page widths, headers, actions, modals, forms, cards and grids.
- [x] Keep dense tables inside touch-scroll containers.
- [x] Run `pnpm test:mobile-layout` and confirm shell assertions pass.

### Task 3: Inbox mobile cards

**Files:**
- Modify: `src/frontend/src/styles/universal-mobile-layout.css`
- Test: `scripts/test-universal-mobile-layout.mjs`

**Interfaces:**
- Consumes: existing `.message-row` children from `InboxPage.tsx`.
- Produces: mobile grid areas for selection, status, sender, subject, attachment, time and actions.

- [x] Extend the regression test with inbox grid-area assertions.
- [x] Confirm the new assertions fail.
- [x] Implement mobile card layout, visible subject, footer metadata and wrapped bulk actions.
- [x] Run the regression test and frontend build.

### Task 4: Production verification and package

**Files:**
- Modify only if validation exposes a scoped regression.

**Interfaces:**
- Consumes: completed frontend changes.
- Produces: validated `artifacts/ionos` production folder without ZIP.

- [ ] Run lint, mobile regression test and frontend build.
- [ ] Run the production workflow and backend smoke suites.
- [ ] Confirm `wwwroot/index.html`, API DLLs and `web.config` exist.
- [ ] Publish the branch commit and provide the exact PowerShell deployment commands.
