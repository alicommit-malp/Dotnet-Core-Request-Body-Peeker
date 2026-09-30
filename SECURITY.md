# Security policy

## Supported versions

| Version | Supported |
| --- | --- |
| 2.x | Yes |
| 1.x | No. Upgrade to 2.x; 1.x depends on a deprecated package with a known vulnerable transitive dependency. |

## Reporting a vulnerability

Please do not open a public issue for security problems.

Use GitHub's private vulnerability reporting on this repository ("Security" tab, "Report a vulnerability"). If that is not available, contact the maintainer through the email address on the GitHub profile linked from the repository.

You can expect an acknowledgement within a week. Fixes are released as a patch version and noted in `CHANGELOG.md`.

## Scope and design notes

- The library has no third-party dependencies. It relies only on the ASP.NET Core shared framework, so framework security updates apply automatically.
- Peeking buffers the whole request body, in memory up to `PeekOptions.BufferThreshold` and then in a temporary file, exactly as ASP.NET Core's own `EnableBuffering` does. Set `PeekOptions.BufferLimit` on endpoints exposed to untrusted clients so a large body cannot exhaust memory or disk. Bodies above the threshold are written unencrypted to the server's temporary directory for the lifetime of the request.
- The string and bytes methods hold one additional copy of the body in memory. Their size is bounded only by `BufferLimit` or the server's request size limit.
- JSON deserialization uses System.Text.Json with the application's configured options, including its default maximum depth.
