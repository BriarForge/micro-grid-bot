# AGENTS.md

## Git identity and wrappers (mandatory)

All git activity in this repo MUST go through a per-person wrapper. No bare `git push`.

| Agent   | Wrapper         |
| ------- | --------------- |
| Aoife   | `git-aoife`     |
| Declan  | `git-declan`    |
| Milena  | `git-milena`    |
| Sofia   | `git-sofia`     |
| Vladislava | `git-vladislava` |
| Mikhail | `git-mikhail`   |
| Nadia   | `git-nadia`     |
| Viktor  | `git-viktor`    |

Whoever pushes uses their own wrapper. Example: Declan pushes with `git-declan push`, Aoife with `git-aoife push`. Wrappers set committer identity and route the push to the correct per-person remote on the matching `github-<person>` SSH host.

Run `git-<person> whoami` to confirm before pushing.

### Wrapper fallback

If none of the per-person wrappers above is installed or discoverable on the current machine,
GitHub CLI may be used with the default `McoreD` account:

1. Run `gh auth status` and verify that `McoreD` is the active account.
2. Set the local commit identity to the authenticated `McoreD` account without changing global Git configuration.
3. Use normal local Git commands for staging and committing, then push through the GitHub CLI-authenticated HTTPS remote.
4. Never use this fallback when a listed per-person wrapper is available.

This fallback does not relax secret-handling or review requirements. Populated `.env` files must remain ignored and untracked.

## Source of truth

Inherited from `/Users/mike/Projects/BriarForge/AGENTS.md`. When this file and the parent conflict, the parent wins until this file is updated to match.

## Project notes

Grid trading bot for the OKX exchange. Implementation language: .NET (C#). Exchange integration: [CryptoExchange.Net](https://github.com/JKorf/CryptoExchange.Net).

Design and scope are being discussed with the Hermes team; this scaffold is just the repo shell so the code can land somewhere.
