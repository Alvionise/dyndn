# Security Policy

## Supported versions

Only the latest release is supported with security fixes.

## Reporting a vulnerability

Please do not open a public issue for security problems. Instead, report it privately via
GitHub Security Advisories: **Security → Report a vulnerability** on
https://github.com/Alvionise/dyndn.

Include steps to reproduce, the affected version, and any relevant logs. You will get a
response as soon as possible.

## Handling of credentials

- The router password in the `routers` table of `config/dyndns.db` is encrypted at rest with Windows
  DPAPI (current-user scope): the value is only decryptable by the Windows account that saved it.
  A value without the `dpapi:` prefix is read as plaintext, which is what a password written into the
  database by hand looks like, and what the app stores when DPAPI is unavailable (it says so in the log).
- Because DPAPI is user- and machine-bound, a database copied to another machine or user account
  cannot be decrypted; the password must be re-entered there.
- The database (`config/dyndns.db` with its side files: `-journal` while a write is in progress and
  `-wal`/`-shm` if the write-ahead log is ever enabled) and the log file (`config/dyndns.log`) are
  git-ignored. Never commit them.
- The log file may contain router addresses and error details. Review it before sharing.
