# Error Handling

The assistant separates a **technical exception** (what actually went wrong) from a
**user-safe error** (what the UI may show). Users never see exception types, stack traces, file
paths, or configuration values.

## Core exceptions

`WindowsAIAssistant.Core/Exceptions` defines the expected failure hierarchy:

```
Exception
   └── AssistantException
         ├── AIServiceException
         └── WindowsServiceException
```

`ConversationNotFoundException` (in the Application layer) also derives from
`AssistantException`, so expected application failures share one root.

## ApplicationError

`ApplicationError` is an immutable record in `Application/Common/Errors` with:

- `Code` - a stable identifier such as `AI_REQUEST_FAILED`
- `Message` - a safe message intended for display
- `Type` - an `ErrorType` (Validation, NotFound, Configuration, ExternalService, System,
  Permission, Cancelled, Unknown)
- `IsRetryable` - an optional hint; retry policies belong to a later Infrastructure step

It never carries a stack trace or exception type.

## Exception mapping

`ExceptionMapper.Map` converts a technical exception into an `ApplicationError`. Mapped messages
are fixed literals, so an exception message containing a secret or a private path is never
copied into the user-facing result.

| Exception | Code | Type |
| --- | --- | --- |
| `OperationCanceledException` (incl. `TaskCanceledException`) | `OPERATION_CANCELLED` | Cancelled |
| `ConversationNotFoundException` | `CONVERSATION_NOT_FOUND` | NotFound |
| `KeyNotFoundException` | `NOT_FOUND` | NotFound |
| `OptionsValidationException` | `CONFIGURATION_INVALID` | Configuration |
| `AIServiceException` | `AI_REQUEST_FAILED` | ExternalService |
| `WindowsServiceException` | `SYSTEM_OPERATION_FAILED` | System |
| `UnauthorizedAccessException` | `PERMISSION_DENIED` | Permission |
| `ArgumentException` (incl. `ArgumentNullException`) | `VALIDATION_ERROR` | Validation |
| `IOException` | `IO_OPERATION_FAILED` | System |
| anything else | `UNKNOWN_ERROR` | Unknown |

`AI_REQUEST_FAILED` and `IO_OPERATION_FAILED` are marked `IsRetryable = true` because they are
often transient. Everything else defaults to `false`.

## IErrorHandler

`IErrorHandler` / `ErrorHandler` is the reusable boundary: given an exception and a short,
safe operation name, it logs the technical detail at the right level and returns the safe
`ApplicationError`.

- Cancellation is logged at `Information`, not `Error`.
- Other failures are logged at `Error` with the exception object (first-party exceptions are
  safe to log) together with the mapped code and type.

It is registered in DI by `AddApplication`, so call sites never construct it directly.

## Where mapping happens

Handlers log technical context with their own `ILogger<T>` and rethrow the original exception
(using `throw;`, preserving the stack trace). Mapping to a user-safe `ApplicationError` happens
at the boundary - the app-level unhandled handler, a future UI layer, or any caller using
`IErrorHandler`.

`Result` and `Result<T>` intentionally keep their simple string-error shape. `ApplicationError`
lives in the Application layer (which references Core, not the reverse), so it is used at the
Application boundary rather than being embedded into the Core `Result` type, which would invert
the layering.

## Unhandled exceptions

At the app boundary, `App.OnUnhandledException` logs `Critical` with the mapped error code and
leaves `Handled = false`, allowing the runtime to terminate. This is deliberate: without a
user-facing error surface, continuing after an unknown failure risks an inconsistent state. A
later UI step will add selective handling and a safe error display (no dialogs exist yet).

## Cancellation

Cancellation is a normal outcome, not a failure. It maps to `OPERATION_CANCELLED` /
`ErrorType.Cancelled` and is logged at `Information`. It is never reported as a system failure
or an unknown error.

## Retryability

`ApplicationError.IsRetryable` is a hint only. No retry loop is implemented in this step; retry
policies belong to a later Infrastructure step, and only for genuinely transient failures such
as network AI errors.

## Safe vs unsafe examples

```
Technical log:   AI provider request failed due to timeout.
User-facing:     The AI service could not complete the request.
```

Never surface `System.Net.Http.HttpRequestException: Connection refused at ...` to the user.
