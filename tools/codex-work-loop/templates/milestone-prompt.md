# Milestone prompt

Read `AGENTS.md`, the generated status, roadmap and relevant ADRs. Inspect the
current branch and working tree before editing. Implement one coherent slice
of the next milestone only. Do not access AWS, OnlineOS, Terraform, MCP,
providers or external infrastructure. Validate build, tests and `git diff
--check` sequentially. Stop on a human decision, `ENGINE_CONTRACT_GAP`, a
security concern or an unexplained failure. Create at most the configured
number of Conventional Commits and return a sanitized report.
