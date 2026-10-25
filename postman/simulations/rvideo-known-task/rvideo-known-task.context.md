# RVideo known-task recovery simulation

Created 2026-10-25 from the existing Ai79TaskClient / RVideo79AiVideoService poll contract, not a live provider call.

## Purpose

The provider member exercises the real HTTP client and adapter through the existing worker. The handler in `postman/mocks/rvideo-known-task/default.js` returns Processing then SUCCESS for each persisted video ID; any submission request fails the test. No owner-workspace mock search was performed; this is a repository-local contract fixture.

## Execution

Run `postman simulation run postman/simulations/rvideo-known-task/rvideo-known-task.sim.yaml --port 49196 --no-history --output ndjson`. Set TODOX_RECOVERY_SIM_URL to `http://127.0.0.1:49196/provider` and TODOX_SUPERSEDE_TEST_DB to a fresh empty UUID-named loopback database. Run the Project196EightConfirmedTasks test in TodoX.Web/Tests. GET /provider/metrics verifies poll counts and zero submits. Stop the CLI process after tests; logs belong under artifacts, not Git.

## Assumptions

- 2026-10-25: the test resets mock state and requires exclusive use of this local simulation. It uses no real credentials. Raw status 1 maps to Processing through the current client/adapter.
- 2026-10-25: HTTP polling and asset transfer are real local requests. Runtime credential resolution, local storage and downstream lifecycle hooks are test doubles; the production handler, completion service, wallet logging and version/job repositories execute. The media test double deliberately supports local fixture downloads without weakening production SSRF guards. Billing reserve is forbidden; completion is counted. Production customer debit idempotency and browser rendering are not claimed as E2E coverage.

## Observations

<!-- begin:observations -->
- 2026-10-25: provider member served Processing/SUCCESS polls and asset downloads for all eight existing IDs. Metrics show two polls per ID and zero submits; PostgreSQL holds eight completed selected versions/scenes and unchanged confirmed intents.
<!-- end:observations -->

## What didn't work

- 2026-10-25: CLI 1.75 requires the member path to name config.yaml rather than a directory; directory form reported “Mock file must point to a file”.
- 2026-10-25: intercepted form bodies may be strings; treating req.body as an object returned 400 for valid videoId. The fixture now parses form-encoded strings.
