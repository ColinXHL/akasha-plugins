# Akasha Automation Companion Protocol v1

This document is the interoperability contract between AkashaNavigator and `AkashaAutomation.Worker`. The Worker implementation is under `src/AkashaAutomation.Worker/Bridge`; changes to framing or fields require a protocol version change and integration tests in both repositories.

## Transport and framing

AkashaNavigator creates a random local named pipe and starts the Worker with:

```text
--pipe <name>
--token <one-time-token>
--parent-pid <AkashaNavigator-pid>
--protocol-version 1
```

The Worker connects as a byte-mode pipe client. Every message is framed as:

```text
4-byte little-endian signed payload length
N-byte UTF-8 JSON payload
```

Payload length must be between 1 and 262144 bytes. Screenshots, image matrices, models and other binary data are never sent through this protocol.

The pipe name is restricted to 1-128 ASCII letters, digits, `.`, `_` and `-`. The one-time token must contain 32-512 non-whitespace characters. Unknown, duplicate, missing or invalid launch arguments make the Worker exit before connecting or constructing any future automation service.

## Handshake

The first Worker message is `hello`:

```json
{
  "type": "hello",
  "protocolVersion": 1,
  "token": "<one-time-token>",
  "workerVersion": "1.0.0",
  "parentProcessId": 1234
}
```

AkashaNavigator validates the protocol, token, expected process and session, then replies:

```json
{
  "type": "welcome",
  "protocolVersion": 1,
  "accepted": true
}
```

A rejected handshake sets `accepted` to `false` and may include an error. The Worker accepts no requests until a valid `welcome` is received. Connection and handshake each have a ten-second timeout.

## Requests and responses

Every request has a non-empty correlation ID and method:

```json
{
  "type": "request",
  "correlationId": "request-1",
  "method": "worker.echo",
  "payload": {
    "value": "test"
  }
}
```

A successful response preserves the correlation ID:

```json
{
  "type": "response",
  "correlationId": "request-1",
  "payload": {
    "value": "test"
  }
}
```

An unsuccessful response contains a stable error code:

```json
{
  "type": "response",
  "correlationId": "request-1",
  "error": {
    "code": "method_not_found",
    "message": "Unknown companion method 'worker.example'."
  }
}
```

AkashaNavigator uses a short write-only lock and a background response reader. Multiple requests may be in flight, responses may arrive out of order, and correlation IDs select the correct caller. Ordinary pending calls are bounded with one capacity slot reserved for `automation.emergencyStop` or `worker.shutdown`. A transport failure completes every pending caller with an error.

Worker ordinary-command admission is also bounded. If its command queue is full, the request receives `queue_full` immediately; emergency stop, shutdown and disconnect handling do not enter that queue.

Protocol v1 currently supports:

| Method | Result |
|---|---|
| `worker.echo` | Returns the request payload unchanged. |
| `worker.getStatus` | Returns stable Worker, game-window, capture, OCR, Feature, emergency-stop and last-error status. |
| `worker.shutdown` | Stops command intake, latches emergency stop, releases runtime resources, then acknowledges and exits. |
| `automation.emergencyStop` | Bypasses the normal command queue, latches emergency stop immediately and returns `{ accepted: true, active: true }`. |
| `features.autoPick.getOptions` | Returns the normalized AutoPick options. |
| `features.autoPick.setOptions` | Validates and atomically replaces AutoPick options and user lists. |
| `features.autoPick.setEnabled` | Sets `{ enabled: boolean }` without replacing the remaining options. |
| `features.autoDialogue.getOptions` | Returns the normalized AutoDialogue options. |
| `features.autoDialogue.setOptions` | Validates and atomically replaces dialogue, option, special-scene and VAD options. |
| `features.autoDialogue.setEnabled` | Sets `{ enabled: boolean }`; disabling immediately cancels an active voice wait. |

AkashaNavigator may also send `{ "type": "shutdown" }` when no acknowledgement is required.

The message type vocabulary is `hello`, `welcome`, `request`, `response`, `event` and `shutdown`. Protocol v1 does not yet emit events.

`worker.getStatus` preserves its original top-level compatibility fields and adds stable subsystem objects:

```json
{
  "state": "ready",
  "protocolVersion": 1,
  "workerVersion": "1.0.0",
  "parentProcessId": 1234,
  "startedAtUtc": "2026-07-15T00:00:00+00:00",
  "realInputEnabled": true,
  "emergencyStop": {
    "isActive": false
  },
  "gameWindow": {
    "state": "not_found",
    "isAvailable": false
  },
  "capture": {
    "state": "not_started",
    "isAvailable": false
  },
  "ocr": {
    "state": "not_started",
    "isAvailable": false
  },
  "features": {
    "autoPick": {
      "isEnabled": false,
      "isRunning": false,
      "recognition": {
        "reason": "not_evaluated",
        "intentSubmitted": false
      }
    },
    "autoDialogue": {
      "isEnabled": false,
      "isRunning": false,
      "dialogueRecognition": {
        "uiCategory": "Unknown",
        "options": [],
        "reason": "not_evaluated",
        "intentSubmitted": false,
        "voiceWaitActive": false,
        "voiceWaitFallback": false
      }
    }
  }
}
```

`lastError` is omitted until an error is reported. AutoPick reports the latest text and rule result. AutoDialogue reports Talk classification, recognized option texts, decision, VAD/fallback state, frame sequence and timestamp. An absent game window is normal: the Worker remains `ready`, and real input remains disabled.

## Lifecycle

The Worker exits cleanly when any of these occurs:

- `worker.shutdown` completes;
- a `shutdown` frame arrives;
- the named pipe disconnects;
- the parent process exits;
- the hosting cancellation token is cancelled.

Before a connection is attempted, the Worker confirms that the declared parent PID exists. Parent exit is monitored throughout connection, handshake and request processing. Runtime states follow `created → connecting → handshaking → ready → stopping → stopped`; `running` is reserved for a future attached automation runtime. Pipe reads continue while ordinary commands execute, so disconnect and emergency-stop requests can cancel an active automation command. Shutdown latches emergency stop first, rejects new commands, cancels the active command, discards buffered commands that have not started, and then releases registered runtime resources in reverse registration order. The shutdown response is written only after cleanup completes. Concurrent shutdown callers share the same result.

## Process exit codes

| Code | Meaning |
|---:|---|
| `0` | Clean shutdown, disconnect or parent exit. |
| `2` | Invalid launch arguments. |
| `3` | Parent process unavailable. |
| `4` | Pipe connection failed or timed out. |
| `5` | Handshake rejected or timed out. |
| `6` | Invalid frame or JSON protocol data. |
| `10` | Unexpected Worker failure. |

## Security invariants

- JavaScript never receives the pipe name or token.
- The token must never be included in errors or logs.
- AkashaNavigator owns pipe ACL creation and constant-time token validation.
- The Worker never accepts executable paths, working directories, environment variables or arbitrary command lines through the protocol.
- Capture, OCR, AutoPick, AutoDialogue and input run only inside the Worker. Phase 6 registers `WindowsSendInputService`; it rejects input unless the located game window is the current foreground window. Both Features remain disabled until the Profile-level plugin switches explicitly enable them.
- Structured rolling logs are written below the current user's local application-data directory, never beside the installed Worker executable.
