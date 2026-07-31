using CSweet.WorkManagement.Contracts;

namespace CSweet.Agents.SoftwareArchitect;

internal static class ArchitecturePlanSamples
{
    internal static ArchitecturePlan MinimalValidPlan() =>
        new(
            "Use one cohesive application boundary with explicit domain and infrastructure dependencies.",
            "A product client invokes the application, which owns its data and brokered integrations.",
            [
                new ArchitectureComponent(
                    "Application",
                    "Own the approved product behavior and orchestration.",
                    ["Persistence"],
                    "Application policy depends on an abstract persistence port implemented by infrastructure.",
                    ["Product API"]),
                new ArchitectureComponent(
                    "Persistence",
                    "Persist product state and enforce storage invariants.",
                    [],
                    "Infrastructure implements an application-owned port.",
                    ["State repository"])
            ],
            [
                new ArchitectureInterface(
                    "Product API",
                    "Application",
                    "Product client",
                    "Versioned request and response contract.",
                    "Return bounded validation or availability failures without leaking internals.")
            ],
            ["Product client -> Product API -> Application -> Persistence port"],
            [
                new ArchitectureDecision(
                    "Deployment shape",
                    "Start with a modular monolith.",
                    "No approved scale or isolation requirement justifies distributed deployment.",
                    ["Independent services"],
                    ["Lower operational overhead", "Boundaries remain extractable if evidence changes."])
            ],
            [
                new ArchitectureQualityAttribute(
                    "Maintainability",
                    "A product rule changes without changing infrastructure consumers.",
                    "Keep business policy cohesive and depend on narrow application-owned ports.",
                    "Architecture and unit tests verify dependency direction."),
                new ArchitectureQualityAttribute(
                    "Security",
                    "An unauthorized caller attempts to access product state.",
                    "Authenticate at the boundary and authorize each protected operation.",
                    "Security tests verify denied and least-privilege access paths."),
                new ArchitectureQualityAttribute(
                    "Reliability",
                    "Persistence is unavailable during a state-changing request.",
                    "Fail atomically with bounded retries only for transient operations.",
                    "Fault-injection tests verify no partial state is committed."),
                new ArchitectureQualityAttribute(
                    "Performance",
                    "The product path runs under its expected peak load.",
                    "Keep the request path bounded and measure storage calls before optimizing.",
                    "A representative load test verifies the approved latency target."),
                new ArchitectureQualityAttribute(
                    "Observability",
                    "An operator investigates a failed product request.",
                    "Emit correlated structured logs, metrics, and traces at system boundaries.",
                    "An operational test follows one request from entry to persistence."),
                new ArchitectureQualityAttribute(
                    "Testability",
                    "A product rule changes without requiring external infrastructure.",
                    "Keep policy deterministic behind narrow ports and provide contract fixtures.",
                    "Unit, contract, and end-to-end tests cover the vertical increment.")
            ],
            ["Persistence unavailable: fail the request without partial state."],
            "Introduce the boundary behind the current product flow without destructive data migration.",
            "Release behind a reversible feature flag and observe errors and latency.",
            "Disable the feature flag and restore the previous code path without deleting new data.",
            ["The current storage contract may require compatibility work."],
            ["The approved product brief is authoritative."],
            [],
            [
                new ArchitectureRequirementTrace(
                    "Deliver the approved product behavior.",
                    ["Application"],
                    ["SA-1"])
            ],
            [
                new ArchitectureSprintPlan(
                    1,
                    "Sprint 1 - Walking skeleton",
                    "Deliver one end-to-end demonstrable product path.",
                    null,
                    null,
                    [
                        new ArchitectureTicketPlan(
                            "SA-1",
                            "Deliver the end-to-end product path",
                            WorkItemKinds.Story,
                            WorkPriorities.High,
                            "Implement the smallest coherent vertical slice.",
                            "This establishes the first reversible product path through the approved architecture boundaries.",
                            ["Deliver the approved behavior through the application boundary."],
                            ["The behavior works end to end.", "Failures are bounded and observable."],
                            ["Preserve unrelated public contracts."],
                            ["Add the versioned Product API and persistence port."],
                            [
                                "Keep product policy in one cohesive component.",
                                "Depend on the narrow application-owned persistence abstraction."
                            ],
                            ["Add focused unit, contract, and end-to-end tests."],
                            [],
                            "Use a reversible feature flag; disable it to roll back.",
                            5)
                    ])
            ]);
}
