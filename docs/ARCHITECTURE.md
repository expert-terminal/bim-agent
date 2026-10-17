# Architecture and scope

## Why Revit transactions and Undo appear in BIM-agent

Revit's API requires a `Transaction` for model changes. A transaction is a consistency boundary: commit applies the change; rollback discards it when an operation fails. A committed transaction normally appears as one entry in Revit's Undo menu. BIM-agent batches wall creation into one transaction and uses separate transactions for parameter edits and deletion. [Autodesk transaction API](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/308ebf8d-d96d-4643-cd1d-34fffcea53fd.htm)

This does **not** mean the connector can perform only “undoable actions.” It can read and analyze without a transaction, and future tools can cover views, sheets, families, schedules, exports, worksharing and other supported APIs. Some actions affect files or external services and may not be reversible through Revit Undo; those need explicit effect reporting and recovery plans.

## Request path

1. An MCP client starts `mcp/server.mjs` over stdio.
2. The MCP server exposes a bounded set of typed tools and forwards requests over a current-user-only Windows named pipe.
3. The Revit add-in queues one request and raises `ExternalEvent`.
4. Revit invokes the handler when its UI is ready. The handler reads or changes the active document on the Revit API thread.
5. For writes, the handler validates IDs and inputs, shows a Revit approval dialog, starts a transaction, commits or rolls back, and sends a structured result.

The server does not accept network connections or execute agent-supplied code. A Revit modal state can delay an external event; the client times out after roughly two minutes. Revit must remain open.

## Current boundaries

- Revit 2026 only, Windows only, one active document at a time.
- Element listing is bounded to 200 results; parameter inspection to 300 parameters; wall creation to 50 walls; deletion to 20 requested elements.
- Numeric element IDs are scoped to the current document and may become stale. The agent should query before acting and verify afterward.
- The generic parameter editor operates on one instance parameter with an unambiguous display name. It does not edit element ID parameters, types, family definitions, project settings, or arbitrary files.
- Wall creation handles straight basic walls. Doors, windows, floors, roofs, stairs, structural systems, MEP routes and documentation need dedicated typed operations.
- Approval dialogs show a summary. A richer visual diff and document revision preconditions are planned.
- Revit Undo does not replace backups, worksharing policies or model review.

## Security model

The named pipe is local and restricted to the Windows account that runs Revit. No API key is stored or transmitted by BIM-agent. The MCP client still receives the model data returned by the selected tools, and its own provider's data policies apply. No arbitrary C#, Python, Dynamo script or shell execution tool is exposed. Writes require confirmation in Revit.

Future releases should add document fingerprints, stable handles, granular approval policies, audit logging, signed binaries, threat-model review and tests for malformed/oversized requests before general public use.
