# Project 188 operator reconciliation plan

The exact seven job/version UUIDs and batch logical identities have now been supplied. The prior general plan is superseded by [the exact-target DBeaver README](project188-scenes831-837-README.md) and its three SQL files: supplemental read-only preflight, guarded reconciliation and read-only postcheck.

Do not repeat the old full preflight. Obtain only missing microsecond snapshot/schema/grant/lease facts using the supplemental file. All writes remain disabled until reviewed schema compatibility, individual pre-HTTP no-submit approvals and worker/watchdog fencing are supplied. Lease expiry, no task ID, an internal failure label or a local artifact hash cannot establish those missing facts. No DTO or Submit Guard changes are included, and no production SQL/provider call/render is performed.
