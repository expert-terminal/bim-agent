# Security

BIM-agent controls a live Revit document. Use it only with agents and projects you trust. The current preview exposes a same-user Windows named pipe, no network listener, bounded tools and approval for writes. It does not expose arbitrary code execution.

Do not use this preview on a critical or confidential project without independent review and a backup. Revit model data returned by the tools is visible to the configured agent and may be processed under that agent provider's policies.

For a vulnerability, open a private GitHub security advisory in this repository rather than a public issue. Include the Revit version, the tool call, expected behavior, observed behavior and a minimal reproduction without client project data.
