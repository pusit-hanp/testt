-- New installation only. Never drops/replaces an existing object.
-- Oracle DDL commits; use a fresh session with no unrelated pending changes.
@@_assert-material-target.sql

DECLARE
    V_COUNT NUMBER;
BEGIN
    SELECT COUNT(*) INTO V_COUNT FROM ALL_OBJECTS
    WHERE OBJECT_NAME = 'PULLLIST_COSTROLLUP'
      AND OWNER IN (SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA'), 'PUBLIC');
    IF V_COUNT <> 0 THEN
        RAISE_APPLICATION_ERROR(-20010,
            'PULLLIST_COSTROLLUP already exists or resolves through a synonym. Stop; review existing-table preflight with the DBA.');
    END IF;
END;
/

CREATE TABLE PULLLIST_COSTROLLUP (
    CUST_CODE VARCHAR2(2 BYTE) NOT NULL,
    COSTROLLUP_STATUS VARCHAR2(1 BYTE) DEFAULT 'N' NOT NULL,
    MISLIP_NO_REF VARCHAR2(50 BYTE),
    LAST_UPDATE DATE,
    CHANGE_BY VARCHAR2(50 BYTE),
    FROM_DATE DATE,
    TO_DATE DATE,
    CONSTRAINT PK_PULLLIST_COSTROLLUP PRIMARY KEY (CUST_CODE),
    CONSTRAINT CK_PULLLIST_COSTROLLUP_STATUS CHECK (COSTROLLUP_STATUS IN ('Y', 'N')),
    CONSTRAINT CK_PULLLIST_CR_DATES CHECK (
        (FROM_DATE IS NULL AND TO_DATE IS NULL) OR
        (FROM_DATE IS NOT NULL AND TO_DATE IS NOT NULL AND FROM_DATE <= TO_DATE))
);

COMMENT ON TABLE PULLLIST_COSTROLLUP IS 'Cost Rollup flag per customer, shared across slip prefixes';
COMMENT ON COLUMN PULLLIST_COSTROLLUP.CHANGE_BY IS 'SERVICE or admin EN; nullable. VARCHAR2(50 BYTE) is an implementation choice.';
COMMENT ON COLUMN PULLLIST_COSTROLLUP.MISLIP_NO_REF IS 'First slip that triggered the current service Cost Rollup flag';
COMMENT ON COLUMN PULLLIST_COSTROLLUP.FROM_DATE IS 'Inclusive manual hold start in Oracle server time; both dates NULL means indefinite.';
COMMENT ON COLUMN PULLLIST_COSTROLLUP.TO_DATE IS 'Inclusive manual hold end in Oracle server time; must accompany FROM_DATE.';

PROMPT Created in the verified current MATERIAL schema. Run 04-validate.sql before enabling the feature.
