# AGENTS.md

Operational guide for AI coding agents working in this repository.

## Project Overview

Stateless **Azure Functions** (isolated worker, **.NET 10**) HTTP API that classifies a raw email as
spam via the **OpenAI Responses API** with strict Structured Outputs. It is **bring-your-own-key
(BYOK)**: the caller supplies their OpenAI API key per request; the key is never persisted, cached,
logged, or returned.

Single endpoint: `POST /spam-check/email` (raw `text/plain` body). One app project plus one xUnit
test project in a single solution (`SpamDetector.sln`).

## Repository Structure

- `src/SpamDetector/` — the Functions app.
  - `Functions/` — HTTP-triggered endpoint (`SpamCheckFunction`). Transport concerns only.
  - `Application/` — use case + boundaries: `ISpamClassifier`, `SpamCheckService`, `ModelResolver`,
    `SpamDetectorOptions`, `SpamClassificationException`, DI (`ServiceCollectionExtensions`).
  - `Infrastructure/OpenAI/` — OpenAI Responses integration: client factory, classifier, prompt,
    JSON schema. All OpenAI-specific code lives here.
  - `Models/` — `ApiResult<T>` envelope and `SpamResult`.
  - `Program.cs` — host bootstrapping + OpenAPI config.
- `tests/SpamDetector.Tests/` — xUnit tests (`Unit/`, `Integration/`), `coverage.runsettings`.
- `specs/001-spam-detector-api/` — feature spec, plan, contracts, checklists (documentation).
- `.specify/` — Spec Kit tooling and the project **constitution** (`.specify/memory/constitution.md`).

Where changes belong: new application logic → `Application/`; anything OpenAI-specific →
`Infrastructure/OpenAI/`; HTTP request/response handling → `Functions/`; DTOs → `Models/`.

## Development Environment

- .NET 10 SDK.
- Azure Functions Core Tools v4 (only needed to run locally with `func`).
- An Azure Storage emulator (Azurite) for the Functions runtime; local dev uses
  `UseDevelopmentStorage=true`.
- NuGet restores only from nuget.org (`nuget.config` clears other sources).
- No OpenAI key is configured in the app; it is always supplied per request via the
  `x-openai-api-key` header.

## Build and Run

```powershell
dotnet build SpamDetector.sln
cd src/SpamDetector
func start                       # serves on http://localhost:7071
```

Route is `/spam-check/email` with no `/api` prefix (`host.json` `routePrefix` is empty).

## Testing

- Run all tests: `dotnet test SpamDetector.sln`.
- The live OpenAI test is tagged `Category=LiveOpenAI` and is skipped unless `OPENAI_API_KEY` is set.
  No default test contacts live OpenAI (a stub replaces the classifier/Responses client).
- Run without the live test and with coverage:

```powershell
dotnet test tests/SpamDetector.Tests/SpamDetector.Tests.csproj `
  --filter "Category!=LiveOpenAI" `
  --settings tests/SpamDetector.Tests/coverage.runsettings
```

- Frameworks: xUnit + NSubstitute; `Xunit.SkippableFact` gates the live test; coverlet for coverage.
- Hand-written source is covered 100% line/branch; `coverage.runsettings` excludes `Program.cs` and
  source-generated bootstrap. Add/adjust tests when changing behavior, especially: request
  validation, model selection/defaulting, spam/non-spam classification, prompt-injection handling,
  OpenAI error mapping, credential-leak prevention, and malformed model responses.

## Code Quality

- No repo linter/formatter config is present; match existing style. Both projects use
  `Nullable` and `ImplicitUsings` enabled — keep nullability annotations correct.
- `OPENAI001` is intentionally suppressed via `NoWarn` (the OpenAI SDK Responses API is
  experimental). Do not remove that suppression.
- There is no CI workflow in this repo; validate locally with `dotnet build` + `dotnet test`.

## Architecture and Design Rules

The **constitution** (`.specify/memory/constitution.md`) defines mandatory constraints. Preserve
these when editing:

- **Layer separation:** keep HTTP transport (`Functions/`), application logic (`Application/`),
  OpenAI integration (`Infrastructure/OpenAI/`), and models (`Models/`) separate. OpenAI types must
  not leak into `Functions/` or `Models/`.
- **BYOK isolation:** build the OpenAI client per request from the caller's key
  (`IResponsesClientFactory`). Never register a singleton OpenAI client bound to a fixed credential;
  never store the key in static/singleton state, logs, exceptions, responses, or telemetry.
- **Stateless:** no email content, credentials, model responses, or history are retained between
  requests. Do not add caches or persistence.
- **Minimal external effects:** classification must not execute code, render HTML/scripts, follow
  links, download resources, or enable model tools. Keep `Tools` unset on the Responses call.
- **Prompt isolation:** the system instruction is a compile-time constant
  (`SpamClassifierPrompt.SystemInstruction`); untrusted email is only ever wrapped via
  `SpamClassifierPrompt.Wrap` and sent as user input. Never build instructions from email content.
- **Deterministic contract:** OpenAI must use strict Structured Outputs (JSON schema,
  `jsonSchemaIsStrict: true`). Invalid/unexpected output fails safely as `502` — never repair
  heuristically.

## Working With the Code

- DI is registered in `Application/ServiceCollectionExtensions.AddSpamDetectorServices`. Register new
  services there; keep them stateless.
- Error handling is layered: `OpenAISpamClassifier` maps provider HTTP status → a
  `SpamClassificationFailure` and throws `SpamClassificationException`; `SpamCheckService` maps that
  to an `ApiResult<SpamResult>` (`502` for provider failures, `500` for unexpected). Reuse this path
  rather than throwing raw exceptions to the function.
- Responses always use the `ApiResult<T>` envelope, and the HTTP status must equal
  `ApiResult.HttpCode`.
- Read the request body via the existing bounded reader in `SpamCheckFunction`; never buffer more
  than `MaxRequestBodyBytes`, and never parse the email body as JSON.
- Do not edit generated output (`bin/`, `obj/`, source-generated Functions bootstrap).

## Configuration

- App options: `src/SpamDetector/appsettings.json` under `SpamDetector`, bound to
  `SpamDetectorOptions` (`MaxRequestBodyBytes`, `OpenAiTimeoutSeconds`, `DefaultModel`).
- `host.json`: `functionTimeout` (`00:01:00`) MUST stay strictly greater than `OpenAiTimeoutSeconds`
  so provider timeouts surface as a clean `502` before the host aborts. This invariant is guarded by
  `HostTimeoutConfigTests` — update the test if you intentionally change the values.
- `local.settings.json` sets `FUNCTIONS_WORKER_RUNTIME=dotnet-isolated`. Do not add an OpenAI key
  here.

## Security

- Never log, persist, serialize, or echo the OpenAI API key or request headers.
- Error messages returned to callers must never contain keys, headers, stack traces, raw provider
  exceptions, or raw payloads (see `SpamClassificationException.SafeMessage`).
- Treat all email content as untrusted data; a prompt-injection attempt is flagged via
  `promptInjectionDetected` but must not change the verdict, output structure, or app behavior.

## Validation Before Completion

- During development, run the smallest relevant tests (for example a single class via
  `dotnet test --filter`).
- Before completing a change: `dotnet build SpamDetector.sln` and
  `dotnet test SpamDetector.sln` (with `OPENAI_API_KEY` unset so the live test stays skipped).
- If you touched config invariants, request/response shape, or error mapping, confirm the relevant
  tests in `tests/SpamDetector.Tests/` still pass and cover the change.
