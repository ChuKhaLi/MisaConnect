# Contracts — MISA eSign PDF Sign Flow (slice 1)

This directory captures the three contract surfaces this slice introduces:

| File | What it pins |
|------|--------------|
| [public-surface.md](./public-surface.md) | The .NET types consumers depend on: `IMisaESignClient`, `services.AddMisaConnectESign(...)`, `MisaESignOptions` (+ nested options), the swappable port interfaces, and the typed exception hierarchy. Every change here is a semver event for the `MisaConnect.ESign` NuGet package. |
| [wire-envelopes.md](./wire-envelopes.md) | The request/response shape for each of the seven MISA endpoints used in slice 1, reproduced verbatim from [docs/misa-api-reference/Tài liệu tích hợp API eSign RemoteSigning - V2.md](../../../docs/misa-api-reference/T%C3%A0i%20li%E1%BB%87u%20t%C3%ADch%20h%E1%BB%A3p%20API%20eSign%20RemoteSigning%20-%20V2.md). When MISA's published doc and a sandbox response disagree, the Postman tie-breaker rule from [docs/misa-esign-spec-plan.md](../../../docs/misa-esign-spec-plan.md) applies. |
| [error-mapping.md](./error-mapping.md) | The MISA `ResponseError.errorCode` → SDK typed exception mapping table. Drives the per-code unit tests required by SC-005. |

Together these three files form the **contract layer** of the slice. The Application orchestrator, Infrastructure wire DTOs, and Client facade are implementations of the contracts pinned here.
