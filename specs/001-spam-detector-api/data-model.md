# Phase 1 Data Model: SpamDetector API

No persistent storage exists (stateless service). These are in-memory transfer/domain models only.

## SpamResult (Models/SpamResult.cs)

The classification result produced by the classifier and returned to the caller. Deserialized
directly from OpenAI's strict Structured Output.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `Spam` | `bool` | required | Email is spam/phishing/scam/unwanted commercial content. |
| `Confidence` | `double` | required, 0.0 ≤ x ≤ 1.0 | Confidence in the classification. |
| `PromptInjectionDetected` | `bool` | required | Email appears to contain an attempt to manipulate/instruct the classifier. |
| `Reason` | `string` | required, non-null (default `""`) | Short explanation of the verdict. |

```csharp
public sealed class SpamResult
{
    public bool Spam { get; set; }
    public double Confidence { get; set; }
    public bool PromptInjectionDetected { get; set; }
    public string Reason { get; set; } = string.Empty;
}
```

- **JSON field names**: `spam`, `confidence`, `promptInjectionDetected`, `reason` (camelCase). These
  must match the strict schema and the serialized API response.
- **Validation**: Enforced by the OpenAI strict JSON Schema (`additionalProperties: false`, all
  required, `confidence` bounded 0–1). If the response violates the schema or fails to deserialize,
  it is rejected as an upstream error (HTTP 502) — never repaired.

## ApiResult<T> (Models/ApiResult.cs)

Generic response envelope. `T` is `SpamResult` for this endpoint.

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `Result` | `T?` | `null` | Classification result when successful; `null` on error. |
| `HttpCode` | `int` | `200` | HTTP status associated with the operation; equals the actual HTTP response status. |
| `IsError` | `bool` | `false` | Whether the operation failed. |
| `ErrorMessage` | `string?` | `null` | Human-readable, non-sensitive error category when failed; `null` on success. |

```csharp
public class ApiResult<T>
{
    public T? Result { get; set; }
    public int HttpCode { get; set; } = 200;
    public bool IsError { get; set; } = false;
    public string? ErrorMessage { get; set; }
}
```

- **Success invariant**: `Result != null`, `HttpCode == 200`, `IsError == false`, `ErrorMessage ==
  null`.
- **Error invariant**: `Result == null`, `HttpCode ∈ {400, 413, 500, 502}`, `IsError == true`,
  `ErrorMessage` is a safe category string containing no key, header, stack trace, or raw payload.
- **Serialization**: Public properties (not fields) for conventional ASP.NET Core JSON behavior.

## SpamCheckRequest (Application/SpamCheckRequest.cs)

Application-layer input to the classification use case. Constructed by the API layer from the
request; never serialized to the caller; never logged.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `EmailContent` | `string` | non-empty (after body read) | Complete raw email as supplied. Untrusted. |
| `ApiKey` | `string` | required, non-empty | Caller's OpenAI API key (BYOK). Request-scoped secret. |
| `Model` | `string` | non-empty (resolved) | Resolved model: caller value or default `gpt-5.6-luna`. |

```csharp
public sealed record SpamCheckRequest(string EmailContent, string ApiKey, string Model);
```

- **Secret handling**: `ApiKey` is request-scoped only. It must never be persisted, cached, placed
  in static/singleton state, logged, telemetered, or copied into `ApiResult`/exceptions.
- **Model resolution**: If `x-openai-model` is missing/empty/whitespace → `Model = "gpt-5.6-luna"`;
  otherwise pass the caller's value through unchanged (no allow-list).

## ISpamClassifier (Application/ISpamClassifier.cs)

The single external boundary abstraction. Implemented by `OpenAISpamClassifier`; replaced by a fake
in tests.

```csharp
public interface ISpamClassifier
{
    Task<SpamResult> ClassifyAsync(SpamCheckRequest request, CancellationToken cancellationToken);
}
```

- Returns a validated `SpamResult` or throws a categorized exception the application layer maps to a
  controlled `ApiResult` error. The interface exposes no OpenAI-specific types.

## Relationships & flow

```text
HTTP request (raw body + headers)
   → SpamCheckEndpoint  (validate: key present, body non-empty, size ≤ limit; resolve model)
   → SpamCheckRequest
   → SpamCheckService.HandleAsync  → ISpamClassifier.ClassifyAsync
                                        → OpenAISpamClassifier (per-request ResponsesClient,
                                          system instruction + untrusted email, strict schema)
                                        → SpamResult (validated)
   → ApiResult<SpamResult>  → HTTP response (status == HttpCode)
```

## State transitions

None. Each request is independent and stateless; no entity has a persisted lifecycle.
