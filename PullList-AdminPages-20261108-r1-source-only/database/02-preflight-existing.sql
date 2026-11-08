-- Read-only entry point for an existing seven-column PULLLIST_COSTROLLUP table.
-- SQL*Plus / SQLcl / SQL Developer Run Script (F5), in a fresh MATERIAL session.
-- Run 00-preflight.sql first to record and confirm DB_NAME/SERVICE_NAME/environment.
-- No ALTER, UPDATE, DELETE, INSERT, reset, owner conversion, or schema repair is performed.
-- 04-validate.sql rejects noncanonical columns/types/default/PK/status/schedule checks/data.
-- It also lists NULL owners for human review; it never assumes that NULL means SERVICE.
-- If validation fails, STOP. Do not run 01-create-new.sql against an existing table.
SET SERVEROUTPUT ON
WHENEVER OSERROR EXIT FAILURE ROLLBACK
WHENEVER SQLERROR EXIT FAILURE ROLLBACK
@@04-validate.sql
PROMPT Existing-table checks completed. Preserve current business data and review NULL-owner rows.
PROMPT Web CRUD can be installed independently. Service hold/skip needs the separately installed and enabled r3 service.
