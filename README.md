# C-Sweet Software Architect

First-party C-Sweet Software Architect agent, version `0.9.0`, built on .NET 10,
`CSweet.Agent.SDK` 3.12.0, Microsoft Agent Framework Harness 1.15.0, and manifest protocol v2.

The agent converts approved product requirements into maintainable system designs, incremental
sprint plans, and developer-ready tickets. Product and Project Managers retain ownership of
outcomes, priority, scope, and approval.

## Workflow

`software-architecture.design.v2` runs a bounded architecture harness. The harness can read the
authoritative business, organization, team, board, sprint, report, and source-conversation context.
It cannot access files, a shell, the web, skills, background agents, automatic approvals, or any
mutation tool. Its only non-read tool submits a typed draft to the hosting process for validation.

The draft covers system boundaries, cohesive component responsibilities, dependency direction,
interfaces, data flows, decisions and alternatives, quality attributes, failure modes, migration,
rollout, rollback, risks, assumptions, requirement traceability, and incremental sprint tickets.
SOLID principles guide applicable component and code boundaries without creating abstractions for
their own sake.

`software-architecture.publish-plan.v2` is separate deterministic C#. It verifies the approved
plan and its hash, creates outcome Epics, Stories beneath those Epics, and Tasks beneath their
Stories, then creates planned sprints and assigns sprint
scope. Junior-ready tickets include ordered implementation guidance, explicit interface and data
behavior, observable acceptance criteria, relevant failure-path verification, rollback, and a
the team-aware estimate policy. Publication requires the approved repository connection, base branch, and first positive
sprint sequence so later plans can append to an existing board deterministically. Ticket
dependencies are resolved to persisted work-item IDs and rejected when they are unknown, cyclic,
or point from an earlier sprint to a later sprint. When a team has multiple Developers or QA
installations, publication deterministically assigns the next ticket to the least-loaded role
member by estimated points, breaking ties by installation ID. Stable keys make publication safe
under at-least-once delivery and partial retries.

The v1 design and publication capabilities remain available for compatibility. New planning uses
the v2 hierarchy: the complete known scope is represented by sprint-grouped Stories and every Story
is fully decomposed into junior-ready Tasks before publication. Large plans are published through
dependency-ordered, idempotent batches of at most 40 ticket mutations.

The agent never starts or completes sprints, selects staff or repositories, manually reassigns
work, writes code, merges, deploys, or publishes releases. Approved publication only binds each
ticket's Development and QA stages to the Product Manager-authorized active assignment pools.

## Conversations

On onboarding, the agent finds its accountable Product or Project Manager from the authoritative
organization and bounded team roster, opens or reuses a private direct chat, and communicates its
understanding or one missing high-value question. Direct messages are used for clarification,
design review, risk escalation, approval, and publication status. Structured capabilities remain
the authority boundary for designing and publishing work.

## Configuration

- `llmProviderId`: approved C-Sweet provider profile.
- `llmModel`: chat model used for design and conversation.
- `maxContextWindowTokens`: bounded planning context budget; default 32,000.
- `maxOutputTokens`: one-response budget; default 8,000 and always lower than the context budget.
- `defaultSprintLengthDays`: human-inclusive cadence used when a request omits one; default 14.
  Agent-only teams default to one-day dependency-based execution windows and do not receive human
  story-point estimates.
- `customInstructions`: optional style guidance that cannot expand authority.

## Build and test

```powershell
dotnet restore CSweet.Agents.SoftwareArchitect.slnx
dotnet test CSweet.Agents.SoftwareArchitect.slnx --no-restore
dotnet run --project src/CSweet.Agents.SoftwareArchitect -- --self-test
```

The tests require no C-Sweet instance, provider credential, repository connection, or network
access after restore. See [GRANTS.md](GRANTS.md) for the reviewed authority request.
