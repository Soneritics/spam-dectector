# Contract: POST /spam-check/email

The single HTTP contract exposed by SpamDetector. This document is the source of truth for the
endpoint's request/response shape and status codes; see `openapi.yaml` for the machine-readable
version and `spam-result.schema.json` for the strict OpenAI Structured Output schema.

## Request

- **Method**: `POST`
- **Path**: `/spam-check/email`
- **Content-Type**: `text/plain` (raw email; JSON is NOT required and NOT parsed)
- **Body**: The complete raw email as text. May contain plain text, HTML, MIME content, email
  headers, and arbitrary formatting. Treated entirely as untrusted data. Must be non-empty and
  ≤ 1,048,576 bytes (1 MB, configurable).

### Headers

| Header | Required | Description |
|--------|----------|-------------|
| `x-openai-api-key` | Yes | Caller's OpenAI API key (BYOK). Request-scoped; never persisted, logged, or returned. |
| `x-openai-model` | No | OpenAI model to use. Missing/empty/whitespace → default `gpt-5.6-luna`. Passed through otherwise (no allow-list). |

## Response

Always `application/json`, an `ApiResult<SpamResult>` envelope. The HTTP status code equals
`httpCode`.

### 200 OK — successful classification

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

### Error responses

Body shape (result is null):

```json
{
  "result": null,
  "httpCode": 400,
  "isError": true,
  "errorMessage": "Request body is empty."
}
```

| HTTP status | `httpCode` | Trigger | Example `errorMessage` (non-sensitive) |
|-------------|-----------|---------|----------------------------------------|
| 400 | 400 | Missing `x-openai-api-key` | `"Missing required header: x-openai-api-key."` |
| 400 | 400 | Empty request body | `"Request body is empty."` |
| 413 | 413 | Body exceeds max size (1 MB) | `"Request body exceeds the maximum allowed size."` |
| 502 | 502 | Model rejected by the provider (no local allow-list) | `"Upstream classification provider rejected the request."` |
| 502 | 502 | OpenAI auth failure | `"Upstream classification provider authentication failed."` |
| 502 | 502 | OpenAI rate limit (429) | `"Upstream classification provider rate limit exceeded."` |
| 502 | 502 | OpenAI timeout (30s) | `"Upstream classification request timed out."` |
| 502 | 502 | OpenAI service error (5xx) | `"Upstream classification provider error."` |
| 502 | 502 | Malformed/undeserializable structured output | `"Upstream classification returned an invalid response."` |
| 500 | 500 | Unexpected internal error | `"An unexpected error occurred."` |

### Security guarantees (contract-level)

- `errorMessage` never contains the API key, request headers, stack traces, raw OpenAI exceptions,
  or raw request payloads.
- Missing key or empty body are rejected **without** calling OpenAI.
- Every request is independent and stateless; no content or credential is retained.

## Acceptance mapping (from spec)

| Spec scenario | Expected outcome |
|---------------|------------------|
| Legitimate email | 200, `result.spam == false` |
| Obvious phishing/spam | 200, `result.spam == true` |
| Email with injection text ("ignore previous instructions…") | 200, structured result preserved; `promptInjectionDetected` may be `true`; verdict not forced by injected text |
| HTML + headers | 200, classified without caller preprocessing |
| No `x-openai-model` header | 200, default model used |
| Empty body | 400 error result |
| Missing API key | 400 error result, OpenAI not called |
| OpenAI failure | 502 error result, no sensitive details |
