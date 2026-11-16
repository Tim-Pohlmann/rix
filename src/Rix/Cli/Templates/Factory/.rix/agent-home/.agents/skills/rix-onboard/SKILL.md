---
name: rix-onboard
description: Onboard the repository you are working on to the rix factory this run belongs to. Use when asked to onboard, initialize, enroll, or connect a repo to the factory.
---

```sh
"$RIX_BIN" init "$GITHUB_REPOSITORY" --ci-workflow "<CI>"
```

<CI>: The name of the main CI workflow of this repo.
