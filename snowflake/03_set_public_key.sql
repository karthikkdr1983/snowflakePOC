-- =============================================================================
-- TimeWise Snowflake POC - 03 Attach the RSA public key to the service user
--
-- 1. Run scripts\New-SnowflakeKeyPair.ps1. It prints this ALTER USER statement
--    with your key already filled in. Or paste the key by hand: the contents of
--    keys\rsa_key.pub WITHOUT the "-----BEGIN/END PUBLIC KEY-----" lines,
--    joined into one line.
-- 2. Run it as ACCOUNTADMIN (or SECURITYADMIN).
-- =============================================================================

USE ROLE ACCOUNTADMIN;

ALTER USER TIMEWISE_SVC SET RSA_PUBLIC_KEY = 'PASTE_PUBLIC_KEY_BODY_HERE';

-- Check: RSA_PUBLIC_KEY_FP should show a SHA256 fingerprint.
-- The key script prints the same fingerprint, so you can compare them.
DESC USER TIMEWISE_SVC;

-- Key rotation later (no downtime):
--   ALTER USER TIMEWISE_SVC SET RSA_PUBLIC_KEY_2 = '<new key>';
--   switch the API to the new private key, then:
--   ALTER USER TIMEWISE_SVC UNSET RSA_PUBLIC_KEY;
