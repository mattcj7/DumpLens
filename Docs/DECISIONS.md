# DECISIONS.md

## Purpose

Records durable architecture/product decisions for DumpLens.

## D0001 - Desktop-First Local Application

DumpLens will be designed as a desktop-first local case application with optional future agency/team server support.

## D0002 - Offline-First Evidence Handling

Core import, search, reconciliation, review, and reporting must work offline.

## D0003 - Original Evidence Is Immutable

Original source files are copied or referenced read-only, hashed, and never modified.

## D0004 - SQLite Is the Authoritative Local Case Database

SQLite is the local authoritative store. DuckDB may be used as a rebuildable analytical cache.

## D0005 - AI Is Optional and Review-Only

AI output is assistance only. It requires source references and human review before official use.

## D0006 - Reconciliation Must Avoid Overclaiming Deletion

The system uses careful labels such as “possible missing counterpart” or “possible deletion gap” until human review and external confirmation support stronger language.

## D0007 - Reports Must Be Source-Cited

Official exports must cite source imports, hashes, artifacts, timestamps, senders/recipients, and platform information.

## D0008 - Project Structure Is Documented

`Docs/PROJECT_STRUCTURE.md` is the source of truth for repo layout.

## D0009 - Project References Are Documented

`Docs/PROJECT_REFERENCES.md` is the source of truth for allowed project reference direction.

## D0010 - Strong Unit Testing Is Required Throughout Build

Every behavior-heavy ticket should add or update unit tests. Reconciliation, normalization, timestamp handling, AI validation, redaction, and guardrail behavior require focused unit tests.

## D0011 - Diagnostic Logging Is a First-Class Requirement

DumpLens must generate structured, evidence-safe logs that help debug problems while avoiding sensitive evidence leakage.

## D0012 - WPF Selected for Initial Desktop Shell

The initial desktop shell uses WPF so DumpLens can start with a Windows-native application model that supports offline-first investigative workflows while keeping UI concerns separate from application and domain logic.

## D0013 - .NET 9 Selected as Initial Target Framework

The initial target framework is .NET 9. The desktop application targets `net9.0-windows`, and the solution should keep a consistent .NET 9 baseline unless a later recorded decision approves a framework change.

## D0014 - WPF App Acts as Composition Root

The WPF desktop shell may reference `DumpLens.Persistence` only to instantiate existing application-facing services at startup. This exception is limited to composition root wiring inside `DumpLens.App`; business logic stays in application and persistence layers, and view models remain dependent on `DumpLens.Application` contracts.

## D0015 - WPF App May Wire Import Preview Services At Composition Root

The WPF desktop shell may reference `DumpLens.Ingestion` only to instantiate existing `DumpLens.Application.Imports` preview/probe services at startup. This exception is limited to composition root wiring inside `DumpLens.App`; views and view models remain dependent on application-facing contracts and must not take on ingestion logic directly.

## Decision Change Process

If a ticket changes architecture, project references, schema, testing expectations, or logging requirements:

1. Update this file.
2. Update the affected guideline doc.
3. Mention the decision in ticket completion summary.
