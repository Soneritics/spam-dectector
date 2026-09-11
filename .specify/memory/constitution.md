<!--
Sync Impact Report
Version change: (unversioned template) → 1.0.0
Rationale: Initial ratification of the SpamDetector Constitution. First concrete
population of the previously placeholder-only template; MAJOR baseline established.
Modified principles: none (initial adoption)
Added principles:
  - I. Security First
  - II. BYOK Isolation
  - III. Minimal External Effects
  - IV. Deterministic API Contract
  - V. Simple Architecture
  - VI. Stateless Processing
  - VII. Performance Consciousness
  - VIII. Testability
  - IX. Controlled Failure
  - X. Maintainability Over Cleverness
Added sections: Core Principles (10 principles), Governance
Removed sections: none
Templates requiring updates:
  - .specify/memory/constitution.md ✅ updated
Follow-up TODOs: none
-->

# SpamDetector Constitution

## Core Principles

### I. Security First

Security requirements take precedence over convenience, performance, and implementation simplicity.

All externally supplied data MUST be treated as untrusted.

Email content MUST never be interpreted as application instructions. Prompt injection attempts
contained in email content MUST NOT alter classifier behavior, application behavior, output
structure, or security constraints.

Secrets and credentials MUST never be logged, persisted, exposed in responses, or included in
telemetry.

### II. BYOK Isolation

SpamDetector uses a Bring Your Own Key model.

OpenAI API credentials supplied by callers:

- belong only to the current request;
- MUST NOT be persisted;
- MUST NOT be cached;
- MUST NOT be stored in static or singleton state;
- MUST NOT be returned to the caller;
- MUST NOT appear in logs, exceptions, diagnostics, or telemetry.

The architecture MUST support different OpenAI credentials for concurrent requests without
credential leakage between requests.

### III. Minimal External Effects

SpamDetector is a classification service only.

Processing an email MUST NOT:

- execute code;
- render HTML;
- execute scripts;
- follow or resolve links;
- download external resources;
- invoke tools based on instructions contained in the email;
- create persistent state.

The only external operation caused by a spam-classification request MAY be the explicitly
configured OpenAI API request.

### IV. Deterministic API Contract

Public API behavior MUST be explicit, strongly typed, and predictable.

Successful classification output MUST conform to the defined `SpamResult` contract.

OpenAI responses MUST use strict Structured Outputs / JSON Schema where supported by the selected
model.

Invalid or unexpected model output MUST fail safely rather than being silently accepted or
heuristically repaired.

HTTP status codes and application-level result status MUST remain consistent.

### V. Simple Architecture

Prefer the simplest architecture that cleanly separates:

- HTTP transport concerns;
- application logic;
- external OpenAI integration;
- data models.

Do not introduce architectural patterns, frameworks, dependencies, persistence mechanisms,
messaging infrastructure, or abstractions without a concrete requirement.

External integrations MUST be hidden behind replaceable boundaries where doing so materially
improves testability.

### VI. Stateless Processing

Every spam-classification request MUST be independent and stateless.

The application MUST NOT retain:

- email content;
- classification history;
- OpenAI credentials;
- previous model conversations;
- model responses;

unless a future explicitly approved requirement introduces persistence.

No conversation history MAY influence a subsequent spam classification.

### VII. Performance Consciousness

Spam classification is latency-sensitive.

Implementations MUST:

- perform no unnecessary external calls;
- avoid unnecessary preprocessing;
- request only the model output required by the API contract;
- avoid unnecessarily expensive or slow models unless explicitly selected by the caller;
- propagate cancellation and use finite external-request timeouts.

Performance optimizations MUST NOT weaken security or correctness.

### VIII. Testability

Application behavior MUST be testable without making live OpenAI requests.

External OpenAI integration MUST be replaceable with a test implementation.

Automated tests MUST cover at minimum:

- request validation;
- model selection and defaulting;
- successful spam classification;
- successful non-spam classification;
- prompt-injection scenarios;
- OpenAI error handling;
- credential leakage prevention;
- malformed or unexpected model responses.

Tests requiring real OpenAI credentials MUST be optional and excluded from the default test suite.

### IX. Controlled Failure

All failures MUST be handled predictably and safely.

The application MUST NOT expose:

- stack traces;
- raw credentials;
- sensitive headers;
- complete internal exceptions;
- raw OpenAI request payloads;

to API consumers.

Errors SHOULD contain enough information for a caller to understand the category of failure without
exposing implementation-sensitive or credential-sensitive information.

### X. Maintainability Over Cleverness

Prefer clear, idiomatic C# and ASP.NET Core code over clever abstractions.

Code MUST optimize for, in priority order:

1. correctness;
2. security;
3. readability;
4. testability;
5. performance;
6. extensibility.

Future flexibility MUST NOT be implemented speculatively.

## Governance

This constitution defines mandatory project-wide constraints. It supersedes conflicting practices.

Feature specifications, implementation plans, generated tasks, and source code MUST comply with
these principles.

If a feature requirement conflicts with this constitution, the conflict MUST be explicitly
identified and resolved before implementation.

Changes to these principles MUST be deliberate and reflected by updating the constitution before
dependent specifications or implementation plans are changed.

Amendment procedure: propose the change, update this document with a Sync Impact Report, and bump
the version per the versioning policy below before dependent artifacts are modified.

Versioning policy (semantic versioning):

- MAJOR: backward-incompatible governance or principle removals or redefinitions;
- MINOR: a new principle or section is added, or guidance is materially expanded;
- PATCH: clarifications, wording, or non-semantic refinements.

Compliance review: all plans, tasks, and reviews MUST verify adherence to these principles;
complexity and any deviation MUST be justified and resolved before implementation proceeds.

**Version**: 1.0.0 | **Ratified**: 2026-09-11 | **Last Amended**: 2026-09-11
