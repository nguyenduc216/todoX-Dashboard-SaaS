# Task 4 Feature Mapping

The active workflows continue through existing handlers, but usage and billing now have generic contracts.

- Dance Sell KIE motion: `dance_sell` / `dance_sell_motion_video` / `motion_video`, usage unit `credits` when KIE returns credits, otherwise `request`.
- Scene image and avatar image workflows: retain current image router entry point and emit generic provider usage.
- Scene video workflow: retains existing handler path and generic billing adapter.
- Operation logs should be queried from render jobs/events/artifacts, provider usage, and billing records.

Provider submission lease coverage remains from Task 3 for provider-account aware paths. Task 5 should continue migrating any remaining direct provider clients to the same completion orchestration.
