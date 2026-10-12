-- =============================================================================
-- TimeWise Snowflake POC - 01 Setup
-- Run in Snowsight as ACCOUNTADMIN (the default role on a free trial).
-- Creates: warehouse, resource monitor, database/schemas, EMPLOYEE table,
--          secure view, least-privilege role and the key-pair service user.
-- Safe to re-run.
--
-- Production note: on the client's account these objects are normally split
-- across SYSADMIN (objects) and USERADMIN/SECURITYADMIN (users, roles, grants).
-- =============================================================================

USE ROLE ACCOUNTADMIN;

-- -----------------------------------------------------------------------------
-- 1. Compute: X-Small warehouse that suspends after 60 seconds idle
-- -----------------------------------------------------------------------------
CREATE WAREHOUSE IF NOT EXISTS TIMEWISE_WH
  WAREHOUSE_SIZE      = 'XSMALL'
  AUTO_SUSPEND        = 60
  AUTO_RESUME         = TRUE
  INITIALLY_SUSPENDED = TRUE
  COMMENT             = 'TimeWise API POC - X-Small, auto-suspend 60s';

-- Hard cap on credits so the POC can never run up a bill.
CREATE RESOURCE MONITOR IF NOT EXISTS TIMEWISE_RM
  WITH CREDIT_QUOTA = 5
       FREQUENCY = MONTHLY
       START_TIMESTAMP = IMMEDIATELY
  TRIGGERS ON 80 PERCENT DO NOTIFY
           ON 100 PERCENT DO SUSPEND;

ALTER WAREHOUSE TIMEWISE_WH SET RESOURCE_MONITOR = TIMEWISE_RM;

-- -----------------------------------------------------------------------------
-- 2. Storage: HR schema holds the raw data, API schema holds what the app reads
-- -----------------------------------------------------------------------------
CREATE DATABASE IF NOT EXISTS TIMEWISE_POC COMMENT = 'TimeWise Snowflake POC';
CREATE SCHEMA   IF NOT EXISTS TIMEWISE_POC.HR  COMMENT = 'Source HR data (not visible to the API)';
CREATE SCHEMA   IF NOT EXISTS TIMEWISE_POC.API COMMENT = 'Secure views exposed to the TimeWise API';

CREATE TABLE IF NOT EXISTS TIMEWISE_POC.HR.EMPLOYEE (
    EMPLOYEE_ID  NUMBER(10,0)  NOT NULL PRIMARY KEY,
    EMAIL        VARCHAR(256)  NOT NULL UNIQUE,
    FIRST_NAME   VARCHAR(100)  NOT NULL,
    LAST_NAME    VARCHAR(100)  NOT NULL,
    JOB_TITLE    VARCHAR(200),
    DEPARTMENT   VARCHAR(100),
    LOCATION     VARCHAR(100),
    COST_CENTRE  VARCHAR(20),
    MANAGER_ID   NUMBER(10,0),
    HIRE_DATE    DATE,
    IS_ACTIVE    BOOLEAN       NOT NULL DEFAULT TRUE,
    SALARY       NUMBER(12,2)  -- sensitive: deliberately NOT exposed by the view
);

-- -----------------------------------------------------------------------------
-- 3. Secure view: active employees only, no salary, manager name pre-joined.
--    The API role can read this view but has no access to the HR schema.
-- -----------------------------------------------------------------------------
CREATE OR REPLACE SECURE VIEW TIMEWISE_POC.API.V_EMPLOYEE
  COMMENT = 'Employee directory for TimeWise (active only, no compensation data)'
AS
SELECT
    e.EMPLOYEE_ID,
    LOWER(e.EMAIL)                    AS EMAIL,
    e.FIRST_NAME,
    e.LAST_NAME,
    e.JOB_TITLE,
    e.DEPARTMENT,
    e.LOCATION,
    e.COST_CENTRE,
    e.MANAGER_ID,
    m.FIRST_NAME || ' ' || m.LAST_NAME AS MANAGER_NAME,
    LOWER(m.EMAIL)                    AS MANAGER_EMAIL,
    e.HIRE_DATE
FROM TIMEWISE_POC.HR.EMPLOYEE e
LEFT JOIN TIMEWISE_POC.HR.EMPLOYEE m
       ON m.EMPLOYEE_ID = e.MANAGER_ID
WHERE e.IS_ACTIVE;

-- -----------------------------------------------------------------------------
-- 4. Least-privilege role for the API
-- -----------------------------------------------------------------------------
CREATE ROLE IF NOT EXISTS TIMEWISE_API_ROLE COMMENT = 'Read-only access to TimeWise API views';
GRANT ROLE TIMEWISE_API_ROLE TO ROLE SYSADMIN;   -- keep the role hierarchy tidy

GRANT USAGE  ON WAREHOUSE TIMEWISE_WH                 TO ROLE TIMEWISE_API_ROLE;
GRANT USAGE  ON DATABASE  TIMEWISE_POC                TO ROLE TIMEWISE_API_ROLE;
GRANT USAGE  ON SCHEMA    TIMEWISE_POC.API            TO ROLE TIMEWISE_API_ROLE;
GRANT SELECT ON VIEW      TIMEWISE_POC.API.V_EMPLOYEE TO ROLE TIMEWISE_API_ROLE;
-- Note: no grants at all on TIMEWISE_POC.HR.

-- -----------------------------------------------------------------------------
-- 5. Service user: TYPE = SERVICE means no password and no interactive login.
--    The RSA public key is attached in 03_set_public_key.sql.
-- -----------------------------------------------------------------------------
CREATE USER IF NOT EXISTS TIMEWISE_SVC
  TYPE              = SERVICE
  DEFAULT_ROLE      = TIMEWISE_API_ROLE
  DEFAULT_WAREHOUSE = TIMEWISE_WH
  DEFAULT_NAMESPACE = 'TIMEWISE_POC.API'
  COMMENT           = 'TimeWise API service account (key-pair auth)';

GRANT ROLE TIMEWISE_API_ROLE TO USER TIMEWISE_SVC;

-- -----------------------------------------------------------------------------
-- 6. Check: note the account identifier for the API config
--    (format ORGNAME-ACCOUNTNAME, e.g. ABCDEFG-XY12345)
-- -----------------------------------------------------------------------------
SELECT CURRENT_ORGANIZATION_NAME() || '-' || CURRENT_ACCOUNT_NAME() AS ACCOUNT_IDENTIFIER_FOR_CONFIG;
