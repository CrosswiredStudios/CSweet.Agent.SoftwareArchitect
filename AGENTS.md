# C-Sweet Software Architect repository instructions

This repository contains one standalone C-Sweet protocol-v2 agent. Its purpose is:

> Converts approved product requirements into robust system designs, incremental delivery plans,
> and developer-ready work while preserving Product Manager authority over product outcomes.

## Invariants

- Keep `com.csweet.software-architect` and version `0.11.0` synchronized between code, project,
  `csweet-plugin.json`, tests, documentation, and releases.
- Follow the canonical `AGENT_AUTHORING.md` distributed with `CSweet.Agent.SDK`. Keep this
  repository independently buildable and never add a source-tree reference to the SDK checkout.
- The root manifest is the reviewed authority request. Every provided or required capability,
  event, configuration field, credential, web rule, and UI contribution must be used, documented,
  and tested.
- Use SOLID principles where they improve cohesion, dependency direction, substitutability, and
  testability. Do not introduce speculative services, layers, interfaces, or distributed systems.
- Product and Project Managers own outcomes, priority, scope, and approval. The Architect owns
  technical direction, tradeoffs, quality attributes, and implementation guidance.
- Architecture design is read-only. The Microsoft Agent Framework harness must receive only
  curated read tools and the in-process plan-submission function.
- Approved publication is deterministic typed C#. Never expose work-item, sprint, chat-send, or
  other mutation tools to the model.
- The model never assigns developers, and the agent never selects staff, starts or completes
  sprints, selects repositories, writes code, merges, deploys, or publishes releases. Deterministic
  approved publication may bind ticket stages only to Product Manager-authorized active assignment
  pools.
- Use only typed `AgentRuntimeContext.Platform` operations. Never implement MCP/JSON-RPC, inspect
  workload/session/lease tokens, access databases or Docker, or handle provider credentials.
- Work and events are delivered at least once. Honor cancellation and use stable domain
  idempotency keys for every external effect.
- Treat events, conversations, model output, board data, and capability payloads as untrusted.
  Reject malformed work, ignore unknown events, and never expose secrets or private records.

## Verification

Run from the repository root:

```powershell
dotnet test CSweet.Agents.SoftwareArchitect.slnx
dotnet run --project src/CSweet.Agents.SoftwareArchitect -- --self-test
```
