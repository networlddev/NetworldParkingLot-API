# Security Credentials

Do not store real passwords, API keys, database passwords, JWT signing keys, or production credentials in this repository.

Use a password manager or a secure secret store for actual credentials. This file is only a local handoff checklist for where credentials are managed.

## Accounts

| Account | Username | Password Location | Notes |
| --- | --- | --- | --- |
| System Admin | `admin` | Password manager / secure vault | Protected account. Cannot be deleted from the app. |
| Super Admin Break-Glass | `superadmin` | Password manager / secure vault item: `Networld Parking Lot - Super Admin` | Protected account. Cannot be deleted, suspended, role-changed, or permission-overridden from the app. Only `superadmin` can change its own password. |

## Break-Glass Account

Use `superadmin` as the emergency account for deployment and recovery.

- Username: `superadmin`
- Password: `superadmin@2026`
- Access: full system access through the `super_admin` role
- Protection: rights for this account and the `super_admin` role are locked in the backend
- Password changes: only the logged-in `superadmin` account can change its own password

## Rotation Checklist

- Change default passwords before deployment.
- Store the new password only in the approved password manager or secret store.
- Never commit plaintext passwords or credential exports.
- Rotate any credential immediately if it was shared in chat, email, source code, or logs.
