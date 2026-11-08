# Testing Strategy

How we decide what to test, at which layer, and how much. The procedure is the
operative part; the framework table and sources are there for cases it does not
settle.

## Layers

| Layer              | Runs                                                           | Command            | Details                         |
| ------------------ | -------------------------------------------------------------- | ------------------ | ------------------------------- |
| Server unit        | Domain functions and refined types, no I/O                     | `just server-test` | —                               |
| Server integration | A slice's real endpoints against in-memory SQLite              | `just server-test` | [Server Tests](server-tests.md) |
| Client unit        | `update` with a model and message; assert on model and effects | `just client-test` | —                               |
| E2E                | The full stack in Docker, driven by Playwright                 | `just e2e-test`    | [E2E Tests](e2e-tests.md)       |

Client tests need no browser or DOM: effects are data, so a test inspects the
effect `update` returns instead of mocking HTTP
([client architecture](client-architecture.md#effect-system)).

## Decide

Work in order, and stop at the first step that settles the question.

1. **Name the behaviour.** One contract, one mechanism, one failure mode. If it
   is not a contract, do not test it. Trivial mappings, accessors, and view
   markup are not contracts.
2. **Severity.** What does it cost if this regresses and nothing catches it?
   Silently wrong data and cross-user leaks are the top of the scale.
   **Override: severity 4 or 5 means test it, whatever the remaining steps
   say.**
3. **Detection.** Is another gate already catching it? Refined types make
   illegal states unrepresentable, the schema carries constraints, and shared
   middleware maps errors. High detection means a test adds little.
4. **Likelihood.** Read git churn for the file, plus its position in the
   dependency graph. The shared modules (server `Domain/`, `Data/`, and
   `Shared/`; client `shared/`) change rarely and are depended on widely, so
   they yield the most value per test. Slices and pages churn.
5. **Maintenance.** Churn raises the cost of a test, not its likelihood: a
   surface that keeps changing keeps rewriting its tests. Defer tests against
   unstable UI.
6. **Layer.** Pick the cheapest that can catch the behaviour: domain unit, then
   `TestApp` integration (in-memory SQLite on localhost, hermetic), then E2E
   (multi-process, non-hermetic). E2E only for flows that span the real client
   and server.
7. **Budget.** Target roughly 80/15/5 small/medium/large by count. Keep the
   whole suite under about ten minutes. Tolerate no flakes.

Decide by severity, then likelihood, then maintenance. Do not compute a score:
multiplying risk factors is FMEA's documented error, and it lets a frequent
trivial failure outrank a rare catastrophic one.

## Common cases

| Situation                                       | Decision                                                 |
| ----------------------------------------------- | -------------------------------------------------------- |
| Invariant enforced by a refined type            | Unit property test, or none if unrepresentable           |
| Domain arithmetic (totals, aggregation)         | Unit test — the highest-value target                     |
| Validation rule with several branches           | Unit test, grouped                                       |
| Serialisation contract (encoding, status codes) | One integration test                                     |
| Cross-user scoping, silently wrong data         | Mandatory (severity override)                            |
| State machine (load, error, retry, save)        | Unit test                                                |
| Real client-to-server flow                      | Exactly one E2E — the floor for every feature            |
| View, layout, CSS polish                        | No automated test                                        |
| New or churning UI                              | Write the mandated E2E, do not extend it; defer the rest |
| Non-injected clock or network in the way        | Fix the design first, then test                          |
| Trivial getter, setter, or passthrough          | No test                                                  |

## Frameworks

Use only when the procedure above is not decisive.

| Framework                          | Structures                                            | Use when                                                | Ref |
| ---------------------------------- | ----------------------------------------------------- | ------------------------------------------------------- | --- |
| Risk-based testing                 | Effort proportional to risk; product vs project risk  | Framing investment across a feature set                 | 1   |
| FMEA (IEC 60812)                   | Severity, occurrence, detection                       | As a factor checklist; take the override, not the score | 2   |
| Agile Testing Quadrants            | Purpose: supporting vs critiquing, business vs tech   | Looking for a coverage gap, especially exploratory      | 3   |
| Test pyramid / trophy / honeycomb  | Shape and volume by layer                             | Sanity-checking the mix                                 | 4   |
| Google test sizes                  | Size by resources; hermeticity; flake policy          | Choosing and constraining a layer                       | 5   |
| Test behaviour, not implementation | State over interaction; change detectors are negative | Reviewing a proposed assertion                          | 5   |
| Mutation testing                   | Whether tests can detect a fault                      | Reviewing strength by hand; no F#/Gleam tooling         | 6   |
| Test impact / predictive selection | Which tests to run                                    | Only when the suite is minutes-plus — not met here      | 7   |
| Cost-of-defect / shift-left        | Timing economics                                      | Directional argument only; the numbers are unsourced    | 8   |
| DORA                               | Outcome policy                                        | Setting suite policies (speed, flake tolerance)         | 9   |
| Design for testability             | Untestable behaviour is a design defect               | When a test needs a clock, network, or browser          | 10  |

## Decisions

- Each behaviour is asserted at one layer only, unless the boundary itself is
  the failure mode. Server integration is hermetic (in-memory SQLite on
  localhost); E2E is non-hermetic and isolates tests with a user per worker plus
  the dev-only `DELETE /api/test/user-data` reset before each test.
- Assert state and effect targets, never interactions or serialised payload
  bytes. Exact-body assertions are change detectors.
- Test impact analysis and predictive selection are not adopted: the suite runs
  in seconds, and selection trades correctness for speed. Shard if it ever
  slows.
- Mutation testing tooling is not adopted. Apply the question in review instead:
  would this test fail if the behaviour regressed?
- Do not justify work with cost-of-defect multipliers. The commonly cited
  figures are unsourced, and the effect is smallest for small, non-critical
  systems like this one.

## Sources

1. ISTQB Certified Tester Foundation Level syllabus, risk-based testing;
   ISO/IEC/IEEE 29119-3.
2. IEC 60812:2018, <https://www.iso.org/standard/62292.html>; AIAG-VDA FMEA
   Handbook (2019) Action Priority; Bowles, "An Assessment of RPN
   Prioritization" (2003).
3. Lisa Crispin, "Using (and Abusing) the Agile Testing Quadrants"; Bach &
   Bolton, "The Real Agile Testing Quadrants" (2014).
4. Mike Cohn, _Succeeding with Agile_ (2009); Kent C. Dodds, "The Testing
   Trophy"; Spotify, "Testing of Microservices".
5. _Software Engineering at Google_, ch. 11–14,
   <https://abseil.io/resources/swe-book/html/ch11.html>; Google Testing Blog,
   "Just Say No to More End-to-End Tests" and "Change-Detector Tests Considered
   Harmful" (2015), "Flaky Tests at Google and How We Mitigate Them" (2016).
6. Just et al., "Are mutants a valid substitute for real faults in software
   testing?", FSE 2014; Petrovic et al., "State of Mutation Testing at Google";
   PIT, <https://pitest.org/>.
7. Rothermel & Harrold, "A Safe, Efficient Regression Test Selection Technique",
   TOSEM 1997; Machalica et al., "Predictive Test Selection", ICSE 2019,
   <https://arxiv.org/abs/1810.05286>; Memon et al., "Taming Google-Scale
   Continuous Testing", ICSE-SEIP 2017.
8. Boehm, _Software Engineering Economics_ (1981); Boehm & Basili, "Software
   Defect Reduction Top 10 List" (2001); Menzies et al., "Are Delayed Issues
   Harder to Resolve?", EMSE 2017, <https://arxiv.org/abs/1609.04886>.
9. Forsgren, Humble & Kim, _Accelerate_ (2018);
   <https://dora.dev/capabilities/test-automation/>.
10. Meszaros, _xUnit Test Patterns_ (2007), "Humble Object"; Feathers, _Working
    Effectively with Legacy Code_ (2004).
