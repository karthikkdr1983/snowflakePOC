-- =============================================================================
-- TimeWise Snowflake POC - 02 Sample data
-- Run after 01_setup.sql. Re-running replaces the data with the same values.
--
-- Builds 100 fake employees with a realistic reporting line:
--   1      CEO
--   2-5    4 directors (one per department)       -> report to the CEO
--   6-20   15 managers                            -> report to a director
--   21-100 80 individual contributors (ICs)       -> report to a manager
-- Employees 99 and 100 are inactive, to show the secure view filtering them out.
-- All data is deterministic, so test emails stay the same each time you run it.
-- =============================================================================

USE ROLE ACCOUNTADMIN;
USE WAREHOUSE TIMEWISE_WH;
USE SCHEMA TIMEWISE_POC.HR;

TRUNCATE TABLE IF EXISTS EMPLOYEE;

INSERT INTO EMPLOYEE (
    EMPLOYEE_ID, EMAIL, FIRST_NAME, LAST_NAME, JOB_TITLE, DEPARTMENT,
    LOCATION, COST_CENTRE, MANAGER_ID, HIRE_DATE, IS_ACTIVE, SALARY)
SELECT
    id,
    LOWER(first_name || '.' || last_name || '@timewise-demo.com'),
    first_name,
    last_name,
    CASE
        WHEN id = 1   THEN 'Chief Executive Officer'
        WHEN id <= 5  THEN 'Director of ' || department
        WHEN id <= 20 THEN department || ' Manager'
        ELSE ARRAY_CONSTRUCT(
               ARRAY_CONSTRUCT('Software Engineer', 'Senior Software Engineer', 'QA Analyst', 'DevOps Engineer'),
               ARRAY_CONSTRUCT('Accountant', 'Financial Analyst', 'Payroll Specialist', 'Accounts Payable Clerk'),
               ARRAY_CONSTRUCT('Operations Analyst', 'Project Coordinator', 'Service Desk Analyst', 'Logistics Planner'),
               ARRAY_CONSTRUCT('HR Advisor', 'Recruiter', 'L&D Specialist', 'HR Coordinator')
             )[dept_idx][id % 4]::STRING
    END,
    department,
    ARRAY_CONSTRUCT('London', 'Manchester', 'Bengaluru', 'Chennai')[id % 4]::STRING,
    'CC-' || TO_VARCHAR(1000 + COALESCE(dept_idx + 1, 0) * 100),
    manager_id,
    DATEADD(day, -((id * 37) % 3000), '2026-01-05'::DATE),
    id NOT IN (99, 100),
    CASE
        WHEN id = 1   THEN 250000
        WHEN id <= 5  THEN 150000 + id * 1000
        WHEN id <= 20 THEN  90000 + id * 500
        ELSE                45000 + (id * 211) % 30000
    END
FROM (
    SELECT
        id,
        ARRAY_CONSTRUCT('Aarav', 'Olivia', 'Priya', 'James', 'Sophia', 'Rahul', 'Emily', 'Arjun', 'Grace', 'Liam',
                        'Ananya', 'Noah', 'Chloe', 'Vikram', 'Ava', 'Daniel', 'Meera', 'Ethan', 'Isla', 'Karthik')
            [(id - 1) % 20]::STRING                                   AS first_name,
        -- (id-1)%20 picks the first name; this offset keeps every first+last pair unique
        ARRAY_CONSTRUCT('Sharma', 'Smith', 'Patel', 'Jones', 'Iyer', 'Taylor', 'Brown', 'Nair', 'Wilson', 'Reddy',
                        'Evans', 'Khan', 'Thomas', 'Rao', 'Walker', 'Menon', 'White', 'Gupta', 'Hughes', 'Das')
            [((id - 1) + FLOOR((id - 1) / 20) * 3) % 20]::STRING      AS last_name,
        CASE
            WHEN id = 1   THEN NULL
            WHEN id <= 5  THEN 1
            WHEN id <= 20 THEN 2 + (id - 6) % 4
            ELSE               6 + (id - 21) % 15
        END                                                           AS manager_id,
        CASE
            WHEN id = 1   THEN NULL
            WHEN id <= 5  THEN id - 2
            WHEN id <= 20 THEN (id - 6) % 4
            ELSE               ((id - 21) % 15) % 4
        END                                                           AS dept_idx,
        COALESCE(
            ARRAY_CONSTRUCT('Engineering', 'Finance', 'Operations', 'People & Culture')[dept_idx]::STRING,
            'Executive')                                              AS department
    FROM (
        SELECT ROW_NUMBER() OVER (ORDER BY SEQ4()) AS id
        FROM TABLE(GENERATOR(ROWCOUNT => 100))
    )
);

-- -----------------------------------------------------------------------------
-- Checks
-- -----------------------------------------------------------------------------
-- Expect 100 rows in the table and 98 in the view (2 inactive).
SELECT
    (SELECT COUNT(*) FROM TIMEWISE_POC.HR.EMPLOYEE)   AS TABLE_ROWS,
    (SELECT COUNT(*) FROM TIMEWISE_POC.API.V_EMPLOYEE) AS VIEW_ROWS;

-- Test users for the demo: copy these emails into Swagger or the demo page.
SELECT v.EMPLOYEE_ID, v.EMAIL, v.JOB_TITLE, v.DEPARTMENT, v.MANAGER_NAME,
       COUNT(r.EMPLOYEE_ID) AS DIRECT_REPORTS
FROM TIMEWISE_POC.API.V_EMPLOYEE v
LEFT JOIN TIMEWISE_POC.API.V_EMPLOYEE r ON r.MANAGER_ID = v.EMPLOYEE_ID
WHERE v.EMPLOYEE_ID IN (1, 2, 6, 21)
GROUP BY v.EMPLOYEE_ID, v.EMAIL, v.JOB_TITLE, v.DEPARTMENT, v.MANAGER_NAME
ORDER BY v.EMPLOYEE_ID;
