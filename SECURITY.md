# Security Policy

## Reporting a vulnerability

If you discover a security vulnerability in SCLOC-Verse, please report it privately — **do not open a public issue.**

Use **GitHub Security Advisories** (the official channel):

1. Open the **Security** tab of this repository.
2. Click **Report a vulnerability**.
3. Submit the details. Maintainers are notified privately.

## Security model

Client security is **server-side by design**:

- Secrets live only on the server.
- The client never receives privileged credentials (`service_role`, creator tokens, API tokens, etc.).
- Decompiling or sniffing the client is out of scope — hiding client code is not a security boundary.

See [PRIVACY.md](PRIVACY.md) for data-handling details.
