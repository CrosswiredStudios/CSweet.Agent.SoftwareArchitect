# C-Sweet Software Architect

First-party C-Sweet Software Architect agent, version `0.3.0`, built on .NET 10,
`CSweet.Agent.SDK` 2.7.0, Microsoft Agent Framework Harness 1.15.0, and manifest protocol v2.

The agent converts approved product requirements into maintainable system designs, incremental
sprint plans, and developer-ready tickets. Product and Project Managers retain ownership of
outcomes, priority, scope, and approval.

## Workflow

`software-architecture.design.v1` runs a bounded architecture harness. The harness can read the
authoritative business, organization, team, board, sprint, report, and source-conversation context.
It cannot access files, a shell, the web, skills, background agents, automatic approvals, or any
mutation tool. Its only non-read tool submits a typed draft to the hosting process for validation.

The draft covers system boundaries, cohesive component responsibilities, dependency direction,
interfaces, data flows, decisions and alternatives, quality attributes, failure modes, migration,
rollout, rollback, risks, assumptions, requirement traceability, and incremental sprint tickets.
SOLID principles guide applicable component and code boundaries without creating abstractions for
their own sake.

`software-architecture.publish-plan.v1` is separate deterministic C#. It verifies the approved
plan and its hash, creates one architecture Epic, creates independently testable Stories and
necessary prerequisite Tasks, estimates leaf work, creates planned sprints, and assigns sprint
scope. Publication requires the approved repository connection, base branch, and first positive
sprint sequence so later plans can append to an existing board deterministically. Ticket
dependencies are resolved to persisted work-item IDs and rejected when they are unknown, cyclic,
or point from an earlier sprint to a later sprint. Stable keys make publication safe under
at-least-once delivery and partial retries.

The agent never starts or completes sprints, assigns developers, selects repositories, writes
code, merges, deploys, or publishes releases.

## Conversations

On onboarding, the agent finds its accountable Product or Project Manager from the authoritative
organization and bounded team roster, opens or reuses a private direct chat, and communicates its
understanding or one missing high-value question. Direct messages are used for clarification,
design review, risk escalation, approval, and publication status. Structured capabilities remain
the authority boundary for designing and publishing work.

## Configuration

- `llmProviderId`: approved C-Sweet provider profile.
- `llmModel`: chat model used for design and conversation.
- `maxContextWindowTokens`: harness context budget; default 128,000.
- `maxOutputTokens`: one-response and compaction reserve; default 16,000.
- `defaultSprintLengthDays`: cadence used when a request omits one; default 14.
- `customInstructions`: optional style guidance that cannot expand authority.

## Build and test

```powershell
dotnet restore CSweet.Agents.SoftwareArchitect.slnx
dotnet test CSweet.Agents.SoftwareArchitect.slnx --no-restore
dotnet run --project src/CSweet.Agents.SoftwareArchitect -- --self-test
```

The tests require no C-Sweet instance, provider credential, repository connection, or network
access after restore. See [GRANTS.md](GRANTS.md) for the reviewed authority request.
