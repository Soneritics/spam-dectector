# Spam Detector

A stateless **Azure Functions** (isolated worker, .NET 10) HTTP API that classifies a raw email as
spam using the **OpenAI Responses API** with strict Structured Outputs. It follows a
**bring-your-own-key (BYOK)** model: the caller supplies their own OpenAI API key per request, which
is never persisted, logged, or returned.

---

## Features

- **Single endpoint:** `POST /spam-check/email` accepting the raw email body as `text/plain`.
- **BYOK isolation:** the OpenAI client is built per request from the caller's key. No singleton
  client bound to a fixed credential is ever registered, and no key is stored, logged, or echoed.
- **Prompt-injection resistant:** the untrusted email is sent only as delimited user input; a static,
  trusted system instruction is never built from email content. Injection attempts are flagged via
  `promptInjectionDetected` and do not force the verdict.
- **Strict Structured Outputs:** the model must return a schema-validated JSON object
  (`spam`, `confidence`, `promptInjectionDetected`, `reason`).
- **Controlled error contract:** provider failures map to HTTP `502`, unexpected errors to `500`, and
  error messages never leak keys, headers, stack traces, or raw payloads.
- **Bounded input:** requests are limited to 1 MB (configurable) and read without over-buffering.
- **Finite provider timeout:** the OpenAI call has a 30s timeout (configurable), always shorter than
  the Functions host `functionTimeout`, so a provider timeout surfaces as a clean `502`.
- **OpenAPI/Swagger** document generated from the function attributes.

---

## Project structure

```
SpamDetector.sln
├─ src/SpamDetector/
│  ├─ Application/            # Use case + boundaries (ISpamClassifier, SpamCheckService,
│  │                         #   ModelResolver, options, DI registration, error types)
│  ├─ Functions/             # SpamCheckFunction — the HTTP-triggered endpoint
│  ├─ Infrastructure/OpenAI/ # OpenAI Responses client factory, classifier, prompt, schema
│  ├─ Models/                # ApiResult<T> envelope, SpamResult
│  ├─ Program.cs             # Host bootstrapping + OpenAPI configuration
│  ├─ appsettings.json       # SpamDetector options
│  └─ host.json              # Functions host settings (functionTimeout, routePrefix)
├─ tests/SpamDetector.Tests/ # xUnit unit + integration tests (NSubstitute, coverlet)
└─ specs/                    # Feature specification, plan, contracts, checklists
```

---

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
  (for running locally with `func`)
- An OpenAI API key (supplied per request by the caller — not stored in the app)
- An Azure Storage emulator/account for the Functions runtime (local dev uses
  `UseDevelopmentStorage=true`, e.g. [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite))

---

## Configuration

Application options live in `src/SpamDetector/appsettings.json` under the `SpamDetector` section and
bind to `SpamDetectorOptions`:

| Setting | Default | Description |
|---------|---------|-------------|
| `MaxRequestBodyBytes` | `1048576` (1 MB) | Maximum accepted request body size in bytes. |
| `OpenAiTimeoutSeconds` | `30` | Finite timeout applied to the OpenAI classification call. |
| `DefaultModel` | `gpt-5.6-luna` | Model used when the caller omits the `x-openai-model` header. |

Host settings (`src/SpamDetector/host.json`):

- `functionTimeout` is `00:01:00` and MUST stay strictly greater than `OpenAiTimeoutSeconds` so a
  provider timeout returns a controlled `502` before the host aborts the invocation. This invariant
  is guarded by a unit test.
- `extensions.http.routePrefix` is empty, so the route is `/spam-check/email` (no `/api` prefix).

Local runtime settings (`local.settings.json`) set `FUNCTIONS_WORKER_RUNTIME=dotnet-isolated` and the
storage connection. No OpenAI key is configured here — it is always supplied per request.

---

## Build, run, and test

```powershell
# Restore & build
dotnet build SpamDetector.sln

# Run all tests (the live OpenAI test is skipped unless OPENAI_API_KEY is set)
dotnet test SpamDetector.sln

# Run the Functions app locally (from the app folder)
cd src/SpamDetector
func start
```

The app listens locally on `http://localhost:7071` by default.

### Test coverage

Tests use xUnit with NSubstitute; no test contacts live OpenAI (a stub replaces the classifier and
the Responses client). To collect coverage with the provided settings:

```powershell
dotnet test tests/SpamDetector.Tests/SpamDetector.Tests.csproj `
  --filter "Category!=LiveOpenAI" `
  --settings tests/SpamDetector.Tests/coverage.runsettings
```

`coverage.runsettings` excludes the Azure Functions source-generated bootstrap classes and
`Program.cs` (host wiring), which are not meaningfully unit-testable. Hand-written source is covered
at 100% line/branch.

An optional live end-to-end test can be enabled by setting environment variables before running:

```powershell
$env:OPENAI_API_KEY = "sk-..."      # required to un-skip the live test
$env:OPENAI_MODEL   = "gpt-5.6-luna" # optional; defaults to gpt-5.6-luna
dotnet test SpamDetector.sln
```

---

## API

### `POST /spam-check/email`

Classifies a raw email body as spam.

**Request**

- `Content-Type: text/plain` — the complete raw email (plain text, HTML, MIME, headers, etc.). Treated
  entirely as untrusted data. Must be non-empty and ≤ 1 MB.

| Header | Required | Description |
|--------|----------|-------------|
| `x-openai-api-key` | Yes | Caller's OpenAI API key (BYOK). Request-scoped; never persisted, logged, or returned. |
| `x-openai-model` | No | Model to use. Missing/empty/whitespace → default `gpt-5.6-luna`; otherwise passed through unchanged. |

**Response** — always `application/json`, an `ApiResult<SpamResult>` envelope whose HTTP status equals
`httpCode`.

`200 OK`:

```json
{
  "result": {
    "spam": false,
    "confidence": 0.02,
    "promptInjectionDetected": false,
    "reason": "Ordinary personal correspondence with no commercial or malicious indicators."
  },
  "httpCode": 200,
  "isError": false,
  "errorMessage": null
}
```

Error body (`result` is `null`):

```json
{
  "result": null,
  "httpCode": 400,
  "isError": true,
  "errorMessage": "Request body is empty."
}
```

| HTTP status | Trigger | Example `errorMessage` |
|-------------|---------|------------------------|
| `400` | Missing `x-openai-api-key` header | `Missing required header: x-openai-api-key.` |
| `400` | Empty request body | `Request body is empty.` |
| `413` | Body exceeds the max size | `Request body exceeds the maximum allowed size.` |
| `502` | Provider rejected the request/model | `Upstream classification provider rejected the request.` |
| `502` | OpenAI authentication failure | `Upstream classification provider authentication failed.` |
| `502` | OpenAI rate limit (429) | `Upstream classification provider rate limit exceeded.` |
| `502` | OpenAI timeout | `Upstream classification request timed out.` |
| `502` | OpenAI service error (5xx) | `Upstream classification provider error.` |
| `502` | Malformed structured output | `Upstream classification returned an invalid response.` |
| `500` | Unexpected internal error | `An unexpected error occurred.` |

Missing key or empty body are rejected **without** calling OpenAI. `errorMessage` never contains the
API key, headers, stack traces, raw provider exceptions, or raw payloads.

**`SpamResult` fields**

| Field | Type | Description |
|-------|------|-------------|
| `spam` | boolean | Whether the email is spam/phishing/scam/unwanted commercial content. |
| `confidence` | number (0–1) | Confidence in the classification. |
| `promptInjectionDetected` | boolean | Whether the email appears to try to manipulate the classifier. |
| `reason` | string | Short explanation of the verdict. |

### Example

```bash
curl -X POST "http://localhost:7071/spam-check/email" \
  -H "x-openai-api-key: sk-your-key" \
  -H "Content-Type: text/plain" \
  --data-binary "Hi Jane, are we still on for lunch Thursday? - Bob"
```

### OpenAPI

An OpenAPI/Swagger document is generated from the endpoint attributes by the
`Microsoft.Azure.Functions.Worker.Extensions.OpenApi` extension and is served by the app's generated
OpenAPI endpoints when running locally.

---

## Security notes

- **BYOK:** keys are request-scoped and used to build a fresh OpenAI client per request. No key is
  stored, logged, serialized, or copied into responses/exceptions.
- **Prompt isolation:** the trusted system instruction is a compile-time constant; the untrusted email
  is only ever wrapped inside `<untrusted_email>` tags and sent as separate user input. No tools are
  enabled on the model call.
- **Stateless:** every request is independent; no email content or credential is retained between
  requests.

---

## Further documentation

See [`specs/001-spam-detector-api/`](specs/001-spam-detector-api/) for the full feature
specification, implementation plan, endpoint contract, data model, and review checklists.
