# TS esign evict-stale-only Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close TS gaps G1/G2 (see spec).
**Architecture:** `application/ports.ts` (port), `infrastructure/defaults.ts` (in-memory cache), `application/token-manager.ts` (`renew`).
**Tech Stack:** TypeScript, vitest.
**Spec:** `docs/superpowers/specs/2026-11-07-ts-esign-evict-stale-only-design.md`

## Global Constraints
- `deleteIfMatches` is optional; zero runtime dependencies; Prettier/ESLint clean.

## Review Focus
1. A legacy custom cache (no `deleteIfMatches`) whose entry changed to another token — `delete` is not called.
2. Refresh fails while another flow stored a fresh token — returned, no login.
3. Refresh fails, entry still the stale token — evicted, one login (existing behaviour).

### Task 1: port + in-memory cache
- [ ] Tests (new `test/unit/token-cache.test.ts`): in-memory `deleteIfMatches` removes on match, keeps on mismatch, no-op on missing.
- [ ] Add `deleteIfMatches?(key: string, value: string): void | Promise<void>` to `TokenCache`; implement in `createInMemoryTokenCache`.

### Task 2: renew evicts only the stale token
- [ ] Tests in `test/unit/token-manager.test.ts`: (a) refresh 500 after another flow stored fresh `T9` → result `T9`, login 0; (b) legacy cache, entry replaced by an expired `T9` before eviction → `delete` never called, login 1; (c) cache with `deleteIfMatches` → called with the stale value.
- [ ] Implement in `renew()`; CHANGELOG `[0.3.0]`, version 0.3.0 (package.json, `VERSION`, lockfile).
