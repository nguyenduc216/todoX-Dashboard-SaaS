# Phase 5.1 Provider Coverage

Active KIE, YEScale, OpenRouter, and Vertex credential submission paths now use provider account credential resolution instead of global/direct application secrets.

Coverage completed:

- KIE Dance Sell reference and motion paths resolve provider account credentials before client calls.
- YEScale image and scene video paths claim provider account leases and pass resolved API keys to submit/poll.
- OpenRouter image path receives the resolved API key from the image render router.
- Vertex image, Gemini prompt, and marketing brief analyzer paths resolve default provider account credentials.
- PostgreSQL integration tests for wallet locking, provider leases, and callback/poll races are enabled and passing.

Limitations:

- YEScale MCP was unavailable (`fetch failed`), so live YEScale metadata was not reverified in this task.
- Paid provider smoke was not executed.

Final provider coverage status: `source-and-db-hardening-complete; live-paid-smoke-pending`.
