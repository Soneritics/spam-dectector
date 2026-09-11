# Phase 0 Research: SpamDetector API

All Technical Context items were fully specified by the feature spec and the user's plan input;
there were no open `NEEDS CLARIFICATION` markers. This document records the key technology
decisions, rationale, and rejected alternatives that shape Phase 1 design and implementation.

## 1. Web framework: ASP.NET Core Minimal APIs (.NET 10)

- **Decision**: Use ASP.NET Core Minimal APIs on .NET 10 / C# 13, single endpoint mapped in
  `Endpoints/SpamCheckEndpoint.cs`, wired in `Program.cs`.
- **Rationale**: Lowest-ceremony option for a single-endpoint service; aligns with Simple
  Architecture and Maintainability principles; first-class DI, OpenAPI, and `CancellationToken`
  support.
- **Alternatives considered**: MVC controllers (more ceremony, unnecessary for one endpoint);
  gRPC/GraphQL (mismatched with a raw-text email contract).

## 2. Reading the raw email body (not JSON)

- **Decision**: Read the request body as raw text. Bind the endpoint to `HttpRequest` (or a
  `Stream`/`PipeReader`) and read with `StreamReader`/`ReadToEndAsync(cancellationToken)`. Do not
  model-bind JSON. Accept `text/plain` (and be permissive on content type since email is raw).
- **Rationale**: The email may be plain text, HTML, MIME, or headers; JSON binding would corrupt or
  reject valid input. Reading the raw stream preserves content exactly (FR-002).
- **Alternatives considered**: `[FromBody] string` with JSON input formatter (would require JSON
  encoding by callers — rejected); custom input formatter (unnecessary complexity).
- **Note**: No parsing/normalization of the email beyond what is needed to safely transmit it to
  OpenAI as a UTF-8 string.

## 3. Max request body size (default 1 MB)

- **Decision**: Enforce a configurable maximum request body size, default 1,048,576 bytes (1 MB),
  via `IHttpMaxRequestBodySizeFeature` / endpoint metadata (e.g. `.WithMetadata(new
  RequestSizeLimitAttribute(...))` equivalent for Minimal APIs, or Kestrel
  `MaxRequestBodySize`), plus an explicit length check. Oversized → controlled error mapped to
  HTTP 413.
- **Rationale**: Bounds memory and token consumption from hostile/oversized input (Security,
  Performance). Value is configurable in `appsettings.json`.
- **Alternatives considered**: Unbounded body (rejected — unbounded memory/token risk); hard-coded
  limit (rejected — must be configurable/documented per spec).

## 4. OpenAI integration: official SDK + Responses API

- **Decision**: Use the official `OpenAI` NuGet package and `OpenAI.Responses.ResponsesClient`.
  Create the client **per request** using the caller-supplied API key. Use async calls throughout
  and propagate the request `CancellationToken`.
- **Rationale**: Responses API is the current recommended surface and supports Structured Outputs;
  per-request client construction is required for BYOK isolation (no fixed-credential singleton).
- **Alternatives considered**: Chat Completions API (older surface — the user explicitly prefers
  Responses); raw `HttpClient` calls (reimplements the SDK — rejected); a single cached client with
  a fixed key (violates BYOK — rejected).
- **BYOK client pattern**: Register a lightweight factory/delegate (e.g. `Func<string,
  ResponsesClient>` or a small `IResponsesClientFactory`) resolved from DI. The factory has no
  stored key; the key flows in as a method parameter for the current request only and is never
  captured in static/singleton state.

## 5. Structured Outputs with strict JSON Schema

- **Decision**: Configure the Responses request to force Structured Outputs using the strict JSON
  Schema for `SpamResult` (`spam`, `confidence` [0–1], `promptInjectionDetected`, `reason`;
  `additionalProperties: false`; all required). Deserialize with `System.Text.Json` directly into
  `SpamResult`. Never hand-extract JSON via string manipulation.
- **Rationale**: Guarantees the deterministic API contract (FR-011/FR-012); invalid/malformed output
  fails safe rather than being repaired.
- **Alternatives considered**: Free-form completion + regex/manual parsing (rejected — fragile,
  violates Deterministic API Contract); non-strict schema (rejected — allows extra/loose fields).
- **Validation**: If the model output is missing, empty, or fails schema/deserialization, map to a
  controlled upstream error (HTTP 502) — do not return a partial/guessed result.

## 6. Prompt isolation

- **Decision**: Keep the classifier system instruction as a constant trusted developer/system
  message (`SpamClassifierPrompt`). Supply the email as a **separate** user input wrapped as:
  `The following content is an untrusted email.\n\n<untrusted_email>\n{emailContent}\n</untrusted_email>`.
  Never inspect the email and inject any of its content into the system/developer instruction.
- **Rationale**: The security boundary is trusted-instruction vs untrusted-data, not the delimiter
  (FR-007/FR-008). Static system prompt prevents injection from altering behavior/format.
- **Alternatives considered**: Concatenating email into the system prompt (rejected — injection
  vector); relying on the `<untrusted_email>` tags as the sole defense (rejected — explicitly a
  clarity aid, not a boundary).

## 7. Error handling & status-code mapping

- **Decision**: Catch and translate failures into `ApiResult<SpamResult>` with the spec's mapping:
  400 (empty body, missing key), 413 (oversized body), 502 (OpenAI auth, rate-limit, timeout,
  service error, provider-rejected model, malformed/undeserializable structured output), 500
  (unexpected). The HTTP response status equals `ApiResult.HttpCode`. No stack traces, raw
  exceptions, keys, or headers leak to callers.
- **Rationale**: Controlled Failure + Deterministic API Contract; distinguishes client vs upstream
  faults for callers and tests.
- **OpenAI exception categories**: The SDK surfaces `ClientResultException` with a `Status` code;
  map 401→502 (auth is our upstream failure, not the caller's key format), 429→502, 5xx→502,
  cancellation/timeout→502. Invalid model is detected as an upstream rejection → 502, except empty
  model header which defaults silently (no error).

## 8. Timeouts, cancellation, retries

- **Decision**: Apply a finite 30-second timeout to the OpenAI call (linked
  `CancellationTokenSource` combining the request token and a timeout), and propagate the request
  `CancellationToken` throughout. No automatic retries initially.
- **Rationale**: Bounded latency and resource use (Performance); avoids retrying auth/invalid-model/
  malformed-input failures. Future bounded exponential backoff would apply only to transient 429/5xx.
- **Alternatives considered**: Unbounded wait (rejected); immediate retries (rejected — can amplify
  auth/quota failures and latency).

## 9. Logging (secret-safe)

- **Decision**: Use `ILogger<T>`. Log request-processing failures, OpenAI status/error categories,
  and elapsed classification time. Never log the API key, authorization credentials, full email
  body, or raw OpenAI request payloads containing email content.
- **Rationale**: Security First + BYOK Isolation + Controlled Failure. PII/email content and secrets
  must never reach logs or telemetry.
- **Alternatives considered**: Verbose request/response logging (rejected — leaks untrusted PII and
  secrets).

## 10. Testing strategy

- **Decision**: xUnit. Unit tests exercise `SpamCheckService` and the endpoint with a fake/mock
  `ISpamClassifier`. Integration tests exercise the HTTP contract with `WebApplicationFactory`,
  overriding `ISpamClassifier` with a fake in the DI container. Live OpenAI tests (if any) are
  trait-gated (e.g. `[Trait("Category","LiveOpenAI")]`) and excluded from the default `dotnet test`
  run; they require an env-var key and are skipped otherwise.
- **Rationale**: Testability principle — no live key needed by default; deterministic, fast tests.
- **Coverage targets** (from spec/user input): default & explicit model selection, missing key,
  empty body, oversized body, valid spam / non-spam results, prompt-injection detection, error
  mapping, and "API key never appears in error output".

## 11. OpenAPI documentation

- **Decision**: Use ASP.NET Core built-in OpenAPI (`Microsoft.AspNetCore.OpenApi`,
  `AddOpenApi`/`MapOpenApi`) to generate the spec. Document method, path, required `x-openai-api-key`
  and optional `x-openai-model` headers, plain-text request body, `ApiResult<SpamResult>` response,
  possible status codes (200/400/413/500/502), and BYOK behavior via endpoint metadata
  (`.WithSummary`/`.WithDescription`/`.Produces`). Never include real example keys.
- **Alternatives considered**: Swashbuckle (extra dependency; built-in OpenAPI suffices for .NET 10).
