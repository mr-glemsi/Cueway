# Code Signing Policy

Free code signing provided by [SignPath.io](https://signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

## Overview

This document defines the code signing policy for **Cueway**, an open-source media key router and per-tab media controller for Windows licensed under the [MIT License](LICENSE). Official release binaries are built automatically from source on GitHub Actions and cryptographically signed to verify publisher integrity.

## Scope of Signed Artifacts

Signing is restricted strictly to official release builds generated directly from tagged commits (`v*`) on the `main` branch using GitHub-hosted runners:

- `Cueway.exe` (Native AOT Windows x64 executable)
- `Cueway-Setup-x64.exe` (Inno Setup 7 Windows x64 installer)

Pull requests, development branches, forks, and local developer builds are excluded from production code signing.

## Build and Signing Pipeline

1. **Build:** GitHub Actions runs on a GitHub-hosted `windows-latest` runner defined in [.github/workflows/build.yml](.github/workflows/build.yml) to compile `Cueway.exe` via `.NET 10` Native AOT and package `Cueway-Setup-x64.exe` via Inno Setup 7.
2. **Artifact Upload:** Unsigned build outputs are stored as GitHub workflow artifacts directly from the CI job.
3. **Signing Request:** For tagged releases, the workflow submits the GitHub artifact to SignPath using the official `signpath/github-action-submit-signing-request` GitHub Action and an encrypted `SIGNPATH_API_TOKEN` repository secret.
4. **Release:** Signed binaries returned by SignPath are published as official assets on [GitHub Releases](https://github.com/mr-glemsi/Cueway/releases).

## Team Roles and Responsibilities

- **Author / Committer:** Görkem Gedik ([@mr-glemsi](https://github.com/mr-glemsi))
- **Reviewer:** Görkem Gedik ([@mr-glemsi](https://github.com/mr-glemsi))
- **Approver:** Görkem Gedik ([@mr-glemsi](https://github.com/mr-glemsi))

Multi-Factor Authentication (MFA) is enforced on both GitHub and SignPath accounts with repository or signing privileges.

## Privacy and Security

- **Private Key Custody:** Signing keys are generated and stored exclusively inside SignPath's Hardware Security Modules (HSMs). Project maintainers never have direct access to private key material.
- **User Privacy:** Cueway operates locally without telemetry or remote data collection. See [PRIVACY.md](PRIVACY.md) for full details.
- **Reporting Issues:** If you suspect a release binary has been tampered with or encounter an unexpected security warning on an official release, please open an issue at [GitHub Issues](https://github.com/mr-glemsi/Cueway/issues).
