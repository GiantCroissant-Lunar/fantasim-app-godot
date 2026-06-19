# FantaSim Worker Context

The exported app starts separate iii worker roles from the same `sim-worker`
binary. The `agent` role owns `agent.hermes.*`; the `tools` role owns
`fantasim.*` app command and verification functions.

Keep those roles separate. Hermes must not recursively call verification
functions on the same worker process that is blocked running Hermes. Add new
app verification capabilities to the tools role and keep their return values
bounded, structured, and JSON-first.

Prefer the app-owned iii functions for live exported-app work:
`fantasim.command.list`, `fantasim.command.execute`, `fantasim.bundle.list`,
`fantasim.bundle.reload`, `fantasim.view.list`, `fantasim.view.show`, and
`fantasim.view.verify`. For long-running app-owned agent work, prefer
`fantasim.agent.develop_bundle_async` plus `fantasim.agent.job` so the tools
worker is not blocked while Hermes needs verification functions. The shell
scripts are convenience wrappers; the iii functions are the contract Hermes
should rely on. After a fresh exported-app launch, warm the gateway with
`bash scripts/app-command.sh agent.status '{}'` before assuming the `fantasim.*`
functions are registered; `app.health` alone only proves the command surface is
alive.

Bundle-development agent requests should carry `idleTimeoutSeconds` so a quiet
Hermes turn is canceled by the app-owned gateway instead of holding the single
background job slot until the full run timeout.
