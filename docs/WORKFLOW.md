# Antigravity Workflow Architecture

## Rule Discovery & Hierarchy
- Antigravity traverses upward from the current working directory to the repository root.
- It automatically loads and merges all hierarchical rule files found: `AGENTS.md`, `GEMINI.md`, and `.agents/rules/*.md`.
- Loading precedence: Workspace Project Rules > Declared Workspace Configs (`skills.json`/`plugins.json`) > Global Configuration (`~/.gemini/config/`) > Built-in Customizations.
- Hard limits: Each rule file is capped at 24,000 bytes (24 KB); total rules share an aggregate context budget of 20,000 tokens (excess rules are demoted to on-demand file links).

## Skills Organization
- Skills reside in `.agents/skills/<name>/SKILL.md` (or globally in `~/.gemini/config/skills/`).
- Each skill package contains:
  - `SKILL.md`: Frontmatter (`name`, `description`) + procedural workflow instructions.
  - Optional subdirectories: `scripts/` (executable helpers), `examples/`, `resources/`, and `references/`.
- Progressive Disclosure: Only skill names and descriptions are exposed in the system prompt; full content is fetched only upon explicit agent or user invocation.

## Where `/learn` Writes
- When `/learn` is invoked following user corrections or completed setups, Antigravity synthesizes and persists the new instructions into the workspace rule files (`AGENTS.md` / `GEMINI.md`) or dedicated rule files in `.agents/rules/` for project-specific rules, or `~/.gemini/config/` for user-wide guidelines.
