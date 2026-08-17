# Software Architect grants

This document describes the minimum authority requested by package version `0.5.0`.

Durable collaboration uses `communication.coordination.start.v1`,
`communication.coordination.respond.v1`, `communication.coordination.read.v1`, and
`communication.coordination.cancel.v1`. These grants permit only same-organization eligible-agent
sessions and never transfer another participant's work, repository, or publication authority.
Manifest declarations request authority; installation grants and resource scopes remain
authoritative.

## Context and model

- `platform.llm.chat-stream.v1` generates architecture drafts and conversational responses.
- `platform.business-profile.read.v1` grounds designs in the authoritative business.
- `platform.organization.snapshot.read.v1` reads current objectives, workstreams, roles, and
  reporting lines.
- `platform.team-roster.read.v1` finds the bounded accountable Product or Project Manager.

## Conversation and lifecycle

- `communication.chat.read.v1` verifies the source conversation and addressed sender.
- `communication.chat.create.v1` opens or reuses a private manager conversation.
- `communication.message.send.v1` sends idempotent clarifications and status.
- `agent.onboarding.complete.v1` acknowledges the exact durable onboarding event.

## Work management

- `work.board.read`, `work.item.read`, `work.sprint.read`, and `work.sprint.report.read` provide
  design context.
- `work.item.create` and `work.item.estimate` publish planning drafts; `work.item.delivery.finalize` attaches approved executable delivery details, and `work.item.move` promotes only dependency-ready first-sprint work.
- `work.sprint.create` and `work.sprint.scope.manage` publish planned increments.

## Source-control governance

- `git.merge.review.v2` reads the exact candidate SHA, QA evidence, and current team policy when
  this installation represents the canonical team lead.
- `git.merge.authorize.v2` records an expiring approve-or-reject decision for that exact SHA; it
  does not give the agent Git credentials or direct merge authority.
- `source-control.repository.provision.v2` requests a policy-bounded private Managed GitHub
  repository. The trusted provisioner—not this agent—owns provider credentials.

The package does not request item assignment or transition, sprint lifecycle, board creation,
Git workspace, credential, provider API, network, database, deployment, or release authority.
