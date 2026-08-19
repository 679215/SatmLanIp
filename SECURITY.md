# Security Policy

## Supported versions

Latest release on GitHub / Thunderstore only.

## Threat model

Trusted LAN / VPN peers only.

- Room UDP: no auth, no encryption
- Fusion Direct: accepts pending connects without a shared secret; encryption off

Do not port-forward the listen port (default `37241`) to the public Internet.

## Reporting

GitHub security advisories preferred; otherwise contact via the repo / Thunderstore page. No exploit details in public issues.