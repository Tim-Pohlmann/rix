# Agent home

Everything in this folder is copied into the runner's home directory (`~`) before rix starts the
agent, in every run of this factory's rix workflows. Files already in the runner's home are kept.

Put here what the agent should find in its home on every run, for every repo this factory serves,
such as skills or the agent's own settings. Each agent looks in its own place, for example:

- Claude Code: `.claude/skills/<name>/SKILL.md`, `.claude/CLAUDE.md`
- pi: `.agents/skills/<name>/SKILL.md`
- opencode: either skills folder above

The workflows fail while this folder is missing, so keep it, even if this file is all it holds.
