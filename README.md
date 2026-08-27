# Zhuomian

Zhuomian is a proposed spatial Windows desktop workspace for organizing and opening applications, files, folders and links through low-interruption desktop `Space` containers.

The repository is currently in **Phase 0: Specification & Feasibility**. There is no production application yet. Desktop embedding, focus behavior, backdrop effects, packaging and performance must be validated before implementation is treated as viable.

## Start here

- [Development plan](docs/DEVELOPMENT_PLAN.md)
- [Product specification](docs/PRODUCT_SPEC.md)
- [Interaction specification](docs/INTERACTION_SPEC.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Diagnostic contract](docs/DIAGNOSTICS.md)
- [Phase 0 status](docs/PHASE_0_STATUS.md)
- [Roadmap](docs/ROADMAP.md)
- [Audit A-J consolidation](docs/AUDIT_CONSOLIDATION.md)
- [Contributing](CONTRIBUTING.md)
- [Security baseline](SECURITY.md)

The defining product rule is simple: a visible preview Item launches directly, while Space Focus exists to browse the complete collection. Hover must never steal keyboard focus or expand while an external foreground application is in use unless the user has explicitly armed the visible desktop.
