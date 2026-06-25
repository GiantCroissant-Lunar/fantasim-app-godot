# External-Tool JSON Schemas

This folder contains the schema-first contract source of truth for iii
external-tool payloads.

## VPLanet

Schemas live under `Schemas/vplanet/` and are written against JSON Schema
Draft 2020-12. Stable `$id` values use the base URL
`https://schemas.fantasim.local/external-tools/vplanet/`.

| File | Purpose |
|------|---------|
| `vplanet.status.response.schema.json` | `vplanet.status` response shape |
| `vplanet.input-build.request.schema.json` | `vplanet.input.build` request shape |
| `vplanet.input-build.response.schema.json` | `vplanet.input.build` response shape |
| `vplanet.run.request.schema.json` | `vplanet.run` request shape |
| `vplanet.run.response.schema.json` | `vplanet.run` response shape |
| `vplanet.output-parse.request.schema.json` | `vplanet.output.parse` request shape |
| `vplanet.output-parse.response.schema.json` | `vplanet.output.parse` response shape |
| `vplanet.input-bundle.schema.json` | Reusable `inputBundle` object |
| `vplanet.run-result.schema.json` | Reusable `runResult` object |
| `vplanet.output-table.schema.json` | Reusable `outputTable` object |

Response and request schemas reference the reusable nested schemas by relative
`$ref` so a single source of truth is preserved.

### Notes for future codegen

- DTOs are generated on demand rather than checked in. Run:

  ```bash
  task external-tools:vplanet:codegen
  ```

  The default output is
  `project/contracts/App.NodeGraph/ExternalTools/GeneratedOut/vplanet`, which is
  excluded from normal project compile items by `project/Directory.Build.props`.
- The schemas use explicit `object`, `array`, `string`, `number`, `integer`, and
  `boolean` types with `additionalProperties: false` on stable nested objects.
- Extension points are limited to the optional `job_id` request property and to
  the `bodyPaths` map, whose keys are body names and values are path strings.
- The current generator is the local
  `project/tools/App.ExternalTools.CodeGen` tool. quicktype or a Roslyn source
  generator can later replace or wrap the same JSON Schema boundary without
  changing the schema source of truth.
