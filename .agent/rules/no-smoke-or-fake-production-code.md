# No Smoke Or Fake Production Code

Production code must express real product/runtime behavior. Do not add smoke,
demo, fake, placeholder, or one-off verification logic to production paths just
to prove an idea works.

## Rule

- Keep smoke checks, fake providers, demo assets, and verification-only harnesses
  out of production runtime composition.
- Put verification-only code under tests, explicit dev tools, or clearly named
  harnesses such as `project/tests/**`, `project/tools/**`, or `vault/**`
  instructions.
- If a temporary harness is needed, keep it outside normal app startup and remove
  it before the feature is considered complete.
- Production paths such as `project/hosts/complete-app/Host.cs`,
  `project/plugins/**/HostComposition/**`, runtime services, bundle manifests,
  and exported app config must name real product concepts and real dependencies.
- Do not hardcode demo/smoke bundle IDs, resource paths, worker IDs, or preload
  flows in the host. Add generic catalog/config/runtime mechanisms instead.

## Allowed

- Unit/integration tests with fakes, fixtures, and smoke assets.
- Dev-only scripts or tools with names that make the harness explicit.
- Diagnostic commands exposed through the command system when they operate on
  real runtime services and are not required for normal startup.
- Temporary local edits during manual verification, as long as they are reverted
  before commit.

## Not Allowed

- Adding `gpu-demo`, `test`, `smoke`, `fake`, or similar concepts to normal app
  startup to stand in for the real architecture.
- Adding per-feature preload blocks to `Host.cs` when a generic resource catalog
  or consumer-owned dependency is needed.
- Calling a proof-of-concept path "hot reload" when it does not exercise the
  real app surface the user is trying to reload.

## Review Gate

Before committing runtime code, ask:

1. Is this code needed by the product path, not just by verification?
2. Does it name a real domain concept instead of a smoke/demo concept?
3. Could this be a test, tool, or explicit dev harness instead?
4. Would a later agent understand this as architecture rather than scaffolding?

If any answer is no, move the code out of production or redesign the slice.
