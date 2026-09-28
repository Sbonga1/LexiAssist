# LexiAssist deployment checklist

## Before deployment

- [ ] Production database connection configured.
- [ ] Database backup taken.
- [ ] Migration list reviewed and migration procedure prepared.
- [ ] Public HTTPS and certificate configured.
- [ ] Production secrets supplied through the hosting platform's secure mechanism.
- [ ] Persistent Data Protection key directory configured.
- [ ] Persistent private learner-recording directory configured.
- [ ] Application process has the required directory permissions.
- [ ] `AllowedHosts` is configured; use `*` to accept any host header.
- [ ] Production instructional audio supplied separately from the development fixture.

## Deploy

- [ ] Release publish artifact created.
- [ ] Artifact copied without development database, recordings, Data Protection keys, secrets, or temporary files.
- [ ] EF Core migration applied to the backed-up production database.
- [ ] Application started with `ASPNETCORE_ENVIRONMENT=Production`.
- [ ] One-time administrator bootstrap disabled after successful provisioning.

## Smoke test

- [ ] `/health` reports healthy over the deployed host.
- [ ] Login is tested over the deployed HTTPS origin; do not use a direct plain-HTTP Production request as the authentication smoke test.
- [ ] Learner login and dashboard work.
- [ ] Administrator login and management areas work.
- [ ] Psychologist login and assigned referral work.
- [ ] Instructional screening audio uses its protected route.
- [ ] Protected learner-recording access is denied to unauthorized users.

## Manual UAT

- [ ] On the deployed HTTPS origin, a learner grants real microphone permission.
- [ ] Learner records, stops, and saves audio.
- [ ] Recording persists after page reload.
- [ ] Assigned psychologist can play the recording.
- [ ] Unrelated learner and unassigned psychologist are denied.

## After deployment

- [ ] Logs contain useful operational events without passwords, tokens, raw recordings, or unnecessary sensitive response content.
- [ ] Database, recordings, Data Protection keys, and instructional audio are included in backup scope.
- [ ] Retention ownership is assigned.
- [ ] Temporary bootstrap credential configuration is removed.
