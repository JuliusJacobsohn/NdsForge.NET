# AI software development speed: evidence available by 2026-10-31

Research scope: primary empirical sources, distinguishing measured task time, coding activity, accepted changes, and delivered software. The final section gives a separately labelled PMD Blue planning estimate. No local build or gameplay tests were run for this research.

## Main interpretation

Current evidence supports materially faster implementation with agentic workflows in many settings. It does not support a universal multiplier for complete projects. Publication dates are especially misleading here: several papers released or revised in late 2026 principally observe tooling from 2025 and early 2026. Benchmark capability, observed company throughput, causal task speedup, and forecasts of a particular reverse-engineering project are different quantities.

## Primary evidence

| Source and publication | Observation period / design | Result and practical limit |
|---|---|---|
| [METR follow-up, February 24, 2026](https://metr.org/blog/2026-02-24-uplift-update/) | Follow-up begun August 2025; 10 returning and 47 new open-source developers; randomized AI access by task. | Returning developers' estimated time effect was −18% (interval −38% to +9%); new developers −4% (−15% to +9%). METR says selection effects and concurrent-agent time measurement make these unreliable estimates. Participants increasingly refused no-AI work or withheld tasks expected to benefit most. The early-2025 finding of +19% task time should not be treated as a current-agent universal forecast. |
| [Demirer, Musolff & Yang, first May, revised September 2026](https://www.nber.org/papers/w35275) | Matched observational event studies; September revision covers over 500,000 GitHub developers. Earlier May PDF used over 100,000 and covered agent treated windows January 2025–March 16, 2026; do not assume that earlier exact window describes the expanded revision. | Current cumulative commit gains across autocomplete, interactive agents and autonomous agents are about +30%, +180%, +240%. The final cumulative gain attenuates to +80% for projects and +30% for releases. These are activity/output effects, not measurements that a fixed task takes 3.4 times less time. Working paper, not randomized adoption. The older FTC-hosted PDF has superseded +40/+140/+180% figures. |
| [He et al., first July 2, revised October 27, 2026](https://arxiv.org/abs/2607.01904v2) | One AI-forward company, January 2024–April 2026; 802 developers and 196,212 PRs; observational developer-level comparisons using Cursor/Claude Code logs. | Raw PRs per active developer reached 2.09 times the January–April 2025 baseline in April 2026, tenth month of a mandate. Immediate adoption increase was about 12%; gains accumulated with use. Review load per reviewer doubled. Authors distinguish the raw company trend from smaller within-developer estimates and warn that adoption was not randomized. PR throughput is an incomplete productivity proxy, not proof of halved complete-project schedules. |
| [Chen & Stratton, current October 31, 2026; first January 7](https://fion.ac/jellyfish.pdf) | Updated working paper: 445 million events, 720,542 workers, 655 firms, January 2021–August 2026; staggered difference-in-differences. | Agent adoption estimates: +44.0% lines of code, +36.0% commits, +35.7% PRs opened; +13.7% Jira issues resolved (borderline significant), +18.0% epics. Output estimates exclude increases above 30.7%. Review capacity and submitted-code quality constrain translation into shipped work. These are observational estimates; parallel-trend assumptions matter. Do not repeat older October reporting of 718 firms, +30% code and +49% review time as the current paper's figures. |
| [Sarkar, May 8, 2026 version; first November 6, 2025](https://suproteem.is/assets/files/agents.pdf) | Cursor telemetry: 119,960 users in 1,000 sampled firms, plus 323,589 merges from 32 firms for output analysis; exploits beta/full release timing in late 2024/early 2025. | Evidence suggests agents shift work toward delegation, context gathering, planning and evaluation. Greater gains occur with more verifiable work and more experienced workers. This is useful evidence for pairing agents with an expert and strong validation, rather than assuming expertise becomes irrelevant. Not a randomized trial of October 2026 frontier models; output analysis covers a selective subset of firms. |
| [Cui et al., February 2025 accessible draft](https://economics.mit.edu/sites/default/files/inline-files/draft_copilot_experiments.pdf) | Three company field randomized trials, 4,867 developers; Microsoft/Accenture/anonymous company; interventions in 2022–2023. | Pooled completed-task increase +26.08%, standard error 10.3%; less experienced developers generally gained more. Useful stronger causal evidence for earlier Copilot-style assistance. It must not be labelled a trial of 2026 autonomous coding agents just because a journal version appeared later. |
| [Hamza et al., ESEM, October 5, 2026](https://drops.dagstuhl.de/entities/document/10.4230/LIPIcs.ESEM.2026.67) | Longitudinal agent-adoption study, 669 developers/103 repositories, 228 control developers, AIDev dataset. | Coding effort stayed stable; documentation and testing measures improved. Faster, less detailed and more permissive reviews also appeared in controls, so authors do not attribute those review changes to agent adoption. Useful counterweight to claims that every observed review decline is caused by agents. It does not provide a general elapsed-time speedup. |

## Implications for interpreting a Blue Rescue Team estimate

The following are engineering inferences, not effect sizes measured in the studies:

- Separate implementation from discovery and validation. Typed codecs, command-line workflows, repetitive data conversions and tests can benefit strongly once the intended behavior is precise. Unknown binary semantics and sparse test oracles are less readily delegated.
- Invest in verifiable work units: known ROM identities, round-trip tests, differential checks against Red's understood behavior where applicable, emulator state inspection, deterministic scenarios, and save/reload checks. These can improve both agent usefulness and review speed.
- Measure end-to-end accepted work rather than generated lines or commits. Human attention, elapsed calendar time, agent compute time, failed attempts, and rework are separate budgets.
- A small expert-led project may avoid some enterprise approval queues, but still needs correctness and compatibility checks. Therefore enterprise averages can inform expectations without setting either a ceiling or a guaranteed speedup.
- Calibrate with a representative vertical slice before applying a multiplier to months of work. None of these studies measures PMD reverse engineering, DS memory constraints, binary patch correctness, or a complete game-expansion project.

## Source handling

The Chen/Stratton URL is a mutable current-paper link; the version read explicitly says October 31, 2026 and differs materially from earlier press coverage. The He et al. citation is pinned to arXiv v2. The coordinating agent verified the NBER September revision; this research worker could read the older FTC-hosted May PDF but the NBER URL returned HTTP 403. The table uses the coordinator's verified latest NBER sample/results and keeps the earlier observation-window boundary explicit. No evidence dated after October 31, 2026 is used.

## Frontier capability evidence

- [Epoch AI / METR MirrorCode, April 10, 2026](https://epoch.ai/publications/mirrorcode-preliminary-results): an agent reimplemented the approximately 16,000-line gotree application using execute-only access to the original, documentation and extensive tests. Researchers estimated 2–17 weeks for a human; this was not a measured long-task human baseline. The result supports investing in executable specifications and differential tests, not a universal development-speed multiplier.
- [Anthropic source-control case study, October 13, updated October 15, 2026](https://www.anthropic.com/research/redesigning-source-control): seven engineers built Scion, Antique and related systems and completed a migration in five months. Daily company merges increased from approximately 2,500 in May to over 30,000 in September. This is a vendor-reported frontier deployment, not a controlled same-work comparison. The authors explicitly note unusual internal model access and token budgets. Merge growth is not a twelvefold per-engineer productivity measurement.

## Revised PMD Blue planning estimate

These are judgmental planning ranges, not measured effects or commitments. Assume one experienced developer directing sustained frontier-agent work, approximately 25–35 human project hours per week, adequate agent capacity, reuse of the existing NdsForge foundation, one US ROM revision, and early investment in automated emulator and differential validation. They do not assume future model improvements. Time is cumulative from project kickoff; rows are not additive.

| Milestone | Revised range | Scope and confidence |
|---|---|---|
| End-to-end proof | 2–5 working days | Build, boot, one visible edit and basic save/reload. Medium confidence until the actual pipeline is run. |
| Narrow usable modding base | 2–4 weeks | Reproducible CLI build, selected editable content, documented patches and regression checks. Medium-low confidence. |
| Reusable scoped SDK | 6–12 weeks | Supported Pokémon/item/move/dungeon data subsets, selected asset workflows and maintainable patch infrastructure. Not every format or script feature. Low-medium confidence. |
| Focused expansion beta | 3–6 months | Additional species capacity and a deliberately limited set of mechanics/QoL features, with save and gameplay validation. Low confidence; contingent on proving ID, memory and save constraints early. |
| Broad polished expansion platform | 12–24+ months | Extensive content, configuration, compatibility, documentation and regression coverage. Very low confidence; community maturity cannot be assigned a reliable solo schedule. |

Relative to a mainly manual implementation workflow, roughly 2–3x overall acceleration is an optimistic working hypothesis for this particular project; 4x is a favorable scenario, not a baseline. Individual precisely testable implementation tasks may improve much more. This multiplier must not be applied again to the already AI-adjusted milestone ranges above.

An illustrative sensitivity calculation, not a measured PMD work breakdown: if 70% of manual effort becomes 5x faster and the other 30% becomes 1.5x faster, remaining effort is 0.70/5 + 0.30/1.5 = 0.34, or about 2.94x overall. Sequential dependencies, feedback delays and rework can further affect calendar time.

Re-estimate after a 2–5-day representative experiment. In addition to a visible edit, prioritize a targeted additional-species feasibility investigation if roster expansion is central. Record accepted functionality, human attention, agent runtime and rework; a functioning build alone does not validate the later milestones.
