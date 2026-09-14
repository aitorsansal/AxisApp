-- Widens device_tokens.platform to accept 'web' — the webapp registers FCM web-push
-- tokens (Firebase JS SDK, same Firebase project as the Android app) the same way the
-- MAUI app registers Android tokens. Added alongside the webapp's web-push feature
-- (2026-09-14). Run once against an existing project; schema.sql's own definition is
-- already updated to match, for fresh installs.
alter table public.device_tokens drop constraint device_tokens_platform_check;
alter table public.device_tokens add constraint device_tokens_platform_check
  check (platform in ('android', 'windows', 'web'));
