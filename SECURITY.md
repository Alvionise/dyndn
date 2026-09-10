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

- The router password in `dyndns.json` is encrypted at rest with Windows DPAPI
  (current-user scope): the value is only decryptable by the Windows account that saved it.
  A value without the `dpapi:` prefix is treated as legacy plaintext and still accepted.
- Because DPAPI is user- and machine-bound, a config copied to another machine or user
  account cannot be decrypted; the password must be re-entered there.
- Local configuration files (`config/dyndns.json`, `config/dns-list.json`) and log files
  (`config/dyndns.log`) are git-ignored. Never commit them.
- The log file may contain router addresses and error details. Review it before sharing.
