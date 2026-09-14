# Implementation Plan: SpamDetector API

**Branch**: `001-spam-detector-api` | **Date**: 2026-09-11 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-spam-detector-api/spec.md`

## Summary

SpamDetector is a stateless **Azure Functions** application (.NET 10 isolated worker) exposing a
single HTTP-triggered endpoint, `POST /spam-check/email`, that classifies a raw email (treated
entirely as untrusted input) as spam/phishing/scam/unwanted using OpenAI. The service follows a
Bring Your Own Key (BYOK) model: the caller supplies the OpenAI API key per request via the
`x-openai-api-key` header and an optional `x-openai-model` header (default `gpt-5.6-luna`). The email
is sent to OpenAI's Responses API with a trusted system instruction and strict Structured Outputs
(JSON Schema) so the model returns a `SpamResult` (`spam`, `confidence`, `promptInjectionDetected`,
`reason`). The response is wrapped in a generic `ApiResult<SpamResult>` envelope. A thin layered
architecture (Functions/API → Application → Infrastructure/OpenAI → Models) keeps transport,
use-case logic, and external integration separate behind the `ISpamClassifier` boundary, enabling
tests without live OpenAI calls.

## Technical Context

**Language/Version**: C# 13 / .NET 10

**Primary Dependencies**: Azure Functions .NET isolated worker
(`Microsoft.Azure.Functions.Worker`, `Microsoft.Azure.Functions.Worker.Sdk`) with the ASP.NET Core
integration for HTTP triggers (`Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore`,
providing `HttpRequest`/`HttpResponse`, DI, `ILogger<T>`, and `CancellationToken`); `OpenAI` NuGet
package (official SDK, `OpenAI.Responses.ResponsesClient`); ASP.NET Core OpenAPI
(`Microsoft.AspNetCore.OpenApi`) via the ASP.NET Core integration pipeline; xUnit for tests; a
hand-written fake (or Moq/NSubstitute) for `ISpamClassifier`.

**Storage**: N/A — no persistence. The service is stateless; nothing is retained between requests.

**Testing**: xUnit. Unit tests (application layer + the function handler via fake `ISpamClassifier`)
and integration tests (HTTP contract by invoking the function handler with a constructed
`HttpRequest`/`DefaultHttpContext` and a fake `ISpamClassifier`). No real OpenAI key required by
default; any live OpenAI test is trait-gated and excluded from the default run.

**Target Platform**: Azure Functions runtime v4 on the .NET 10 isolated worker (Linux/Windows;
locally via Azure Functions Core Tools `func start`).

**Project Type**: Serverless HTTP service (single Azure Functions project + single test project).

**Performance Goals**: Low latency; exactly one OpenAI classification call per request; minimal model
output; no preprocessing, persistence, queues, or background jobs. Finite 30s OpenAI timeout.

**Constraints**: BYOK key never persisted/logged/returned/telemetered/singleton-registered; email
always untrusted; strict structured output validated before return; configurable max request body
size (default 1 MB / 1,048,576 bytes), enforced by an explicit Content-Length / read-length check in
the function handler (plus host `maxRequestBodySize` where applicable); cancellation propagated
end-to-end; no tools (web/file search, function calling, code exec); no HTML render, script exec,
URL resolution, or remote fetch.

**Scale/Scope**: Single endpoint, single email per request. Stateless; horizontally scalable behind
a load balancer with no shared state.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Evaluated against `.specify/memory/constitution.md` v1.0.0:

| Principle | Compliance in this plan |
|-----------|-------------------------|
| I. Security First | Email is untrusted end-to-end; system instruction is trusted and never built from email content; no secret logged/returned. ✅ |
| II. BYOK Isolation | Key scoped to request; per-request `ResponsesClient` (never singleton with fixed credential); never persisted/cached/static/logged/telemetered/in `ApiResult`. ✅ |
| III. Minimal External Effects | Only external action is the single OpenAI call; no HTML render, script exec, link/URL resolution, remote fetch, or tools; no persistent state. ✅ |
| IV. Deterministic API Contract | Strict Structured Outputs JSON Schema; output validated/deserialized into `SpamResult` before return; invalid output fails safe (502); HTTP code == `ApiResult.HttpCode`. ✅ |
| V. Simple Architecture | 4 logical layers in one Azure Functions project; one boundary interface `ISpamClassifier`; no mediator/bus/repository/DB. ✅ |
| VI. Stateless Processing | No retention of email, history, credentials, conversations, or responses; every request independent. ✅ |
| VII. Performance Consciousness | One call/request, minimal output, default cheap model unless caller overrides, finite timeout, cancellation propagated. ✅ |
| VIII. Testability | `ISpamClassifier` replaceable by fake; required test scenarios enumerated; live OpenAI tests optional/excluded by default. ✅ |
| IX. Controlled Failure | Errors mapped to controlled `ApiResult` with safe categories; no stack traces/raw exceptions/keys/headers/payloads exposed. ✅ |
| X. Maintainability Over Cleverness | Idiomatic isolated-worker Azure Functions C#; no speculative abstractions. ✅ |

**Result**: PASS. No violations; Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/001-spam-detector-api/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   ├── spam-check-endpoint.md
│   ├── openapi.yaml
│   └── spam-result.schema.json
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
└── SpamDetector/
    ├── Application/
    │   ├── SpamCheckRequest.cs        # App request model: raw email, api key, model
    │   ├── ISpamClassifier.cs         # Classification boundary abstraction
    │   └── SpamCheckService.cs        # Spam classification use case → ApiResult<SpamResult>
    │
    ├── Infrastructure/
    │   └── OpenAI/
    │       ├── OpenAISpamClassifier.cs  # ISpamClassifier impl via OpenAI Responses API (per-request client)
    │       ├── SpamClassifierPrompt.cs  # Trusted system instruction + untrusted-email wrapper
    │       └── SpamResultSchema.cs      # Strict JSON Schema for Structured Outputs
    │
    ├── Models/
    │   ├── ApiResult.cs               # Generic result envelope
    │   └── SpamResult.cs              # Classification result model
    │
    ├── Functions/
    │   └── SpamCheckFunction.cs       # HTTP-triggered function POST /spam-check/email; header + body validation
    │
    ├── Program.cs                     # Isolated-worker host: DI, ASP.NET Core integration, OpenAPI, body-size limit
    ├── host.json                      # Functions host configuration
    ├── local.settings.json            # Local dev settings (not committed; no secrets)
    ├── appsettings.json               # Default config (max body size, timeout, default model)
    └── SpamDetector.csproj

tests/
└── SpamDetector.Tests/
    ├── Unit/                          # Application + function-handler tests with fake ISpamClassifier
    ├── Integration/                   # HTTP contract via constructed HttpRequest/DefaultHttpContext + fake classifier
    └── SpamDetector.Tests.csproj

SpamDetector.sln
```

**Structure Decision**: Single production Azure Functions project (`src/SpamDetector`) with internal
folders for the four logical layers (`Application`, `Infrastructure`, `Models`, `Functions`), plus
one test project (`tests/SpamDetector.Tests`). This matches the constitution's Simple Architecture
principle and the user's explicit structure. No assembly split is justified for this scope.

## Complexity Tracking

> No constitution violations. Section intentionally empty.
