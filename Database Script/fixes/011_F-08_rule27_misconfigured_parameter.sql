/* =============================================================================
   011_F-08_rule27_misconfigured_parameter.sql

   RUN AFTER 006. Found by the dry run in 010.

   THE SYMPTOM

       010 reported one rule still not running:

           50002  Rule 27 did not run: no handler for CUSTOM_LOGIC keyword
                  "%[^a-zA-Z0-9 /.,#''&-]%".

   THE CAUSE — a parameter overwritten with the wrong kind of value

       Rule 27 is EMP-008, "Affiliates Without Employees", on
       Staging_Employers.AffiliatedEIN, with the message "This Affiliate EIN has
       no employees assigned to it."

       Its RuleParameter1 should therefore be a CUSTOM_LOGIC keyword. Instead it
       holds a LIKE pattern for character validation — the same shape used by
       rules 28, 68, 69, 70, 75 and 78, which are all REGEX_CONTAINS checks on
       name and address fields. A pattern was pasted over the keyword.

       The engine already contains the handler this rule wants:

           ELSE IF @Param1 = 'Affiliates_MustHaveEmployees'
               ... NOT EXISTS (SELECT 1 FROM Staging_Employees E
                               WHERE E.FileLogId = S.FileLogId
                                 AND E.EINAssociatedWithEE = S.AffiliatedEIN)

       Nothing points at it. It targets exactly this table and column and
       expresses exactly this rule's stated intent, so the fix restores the link
       rather than writing new logic.

   WHY NOT JUST CHANGE ValidationType TO REGEX_CONTAINS

       That was the obvious reading and it is wrong twice over.

       It would make the rule check something its own name, message, help text and
       client-facing notes all say it does not check — the finding would read
       "this Affiliate EIN has no employees" when what was actually detected is an
       odd character.

       Worse, it would become destructive. Rule 27 carries IsAutoCorrect = 1 with
       an empty AutoCorrectReplacement. sp_Validation_AutoCorrect cursors over
       IsAutoCorrect = 1 rules and branches on ValidationType; CUSTOM_LOGIC matches
       no branch, which is the only reason nothing happens today. Under
       REGEX_CONTAINS it would match this branch:

           WHILE @Rows > 0
               UPDATE TOP (5000) T
               SET AffiliatedEIN = UPPER(STUFF(AffiliatedEIN,
                                   PATINDEX(<pattern>, AffiliatedEIN), 1, ''))
               WHERE FileLogId = @FileLogId AND AffiliatedEIN LIKE <pattern>

       — looping until no row matches, deleting every character the pattern
       rejects from the column that binds an affiliate to its parent employer.

   WHAT    1. RuleParameter1 -> 'Affiliates_MustHaveEmployees'.
           2. IsAutoCorrect -> 0 and AutoCorrectReplacement -> NULL. "Affiliate has
              no employees" cannot be auto-corrected; there is nothing to replace.
              Leaving the flag set would arm the trap above for whoever next adds a
              CUSTOM_LOGIC branch to sp_Validation_AutoCorrect.

   WHAT TO EXPECT

       Zero new findings on the data currently in staging. All six distinct
       AffiliatedEIN values across both files have employees pointing at them, so
       a correctly wired rule finds nothing. That is the right answer — it means
       the affiliate structure in the imported filings is sound. The value is that
       the check now exists for future uploads.

   KNOWN EDGE CASE, LEFT ALONE

       For a row where AffiliatedEIN is blank, NOT EXISTS is trivially true and the
       rule reports "this Affiliate EIN has no employees". Imprecise but harmless,
       it is only a Warning, and a blank EIN is already caught by the REQUIRED
       rules. Guarding it would mean replacing the whole 438-line procedure body
       again for no behavioural gain, which is not a trade worth making here.

   SAFE TO RE-RUN
       Yes. Scoped to rule 27 and to the incorrect value.
   ============================================================================= */

USE [db_ACA360];
GO

SET NOCOUNT ON;
GO

/* ---------------------------------------------------------------- PART 1 ----
   Before.
   ---------------------------------------------------------------------------- */
PRINT '--- Rule 27 BEFORE ---';
SELECT  RuleId, RuleCode, RuleName, TargetTable, TargetColumn,
        ValidationType, RuleParameter1, Severity, IsActive,
        IsAutoCorrect, AutoCorrectReplacement
FROM    dbo.ValidationRules
WHERE   RuleId = 27;

PRINT '--- Rules currently armed for auto-correct (watch for CUSTOM_LOGIC here) ---';
SELECT  RuleId, RuleCode, RuleName, ValidationType, TargetTable, TargetColumn,
        AutoCorrectReplacement,
        Note = CASE WHEN ValidationType = 'CUSTOM_LOGIC'
                    THEN 'inert today - would activate if a CUSTOM_LOGIC branch is added'
                    ELSE 'active' END
FROM    dbo.ValidationRules
WHERE   IsActive = 1 AND ISNULL(IsAutoCorrect, 0) = 1
ORDER BY RuleId;

/* ---------------------------------------------------------------- PART 2 ----
   Fix.
   ---------------------------------------------------------------------------- */
BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @Fixed INT;

    UPDATE  dbo.ValidationRules
    SET     RuleParameter1         = 'Affiliates_MustHaveEmployees',
            IsAutoCorrect          = 0,
            AutoCorrectReplacement = NULL
    WHERE   RuleId = 27
      AND   ValidationType = 'CUSTOM_LOGIC'
      AND   RuleParameter1 <> 'Affiliates_MustHaveEmployees';

    SET @Fixed = @@ROWCOUNT;
    PRINT '--- Rules corrected: ' + CAST(@Fixed AS VARCHAR(10)) + ' ---';

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT '--- Rolled back. No changes were made. ---';
    THROW;
END CATCH
GO

/* -------------------------------------------------------------- VERIFY ----- */
PRINT '--- Rule 27 AFTER ---';
SELECT  RuleId, RuleCode, RuleName, ValidationType, RuleParameter1,
        Severity, IsActive, IsAutoCorrect
FROM    dbo.ValidationRules
WHERE   RuleId = 27;

PRINT '--- Every active CUSTOM_LOGIC keyword in use ---';
PRINT '    Each must match an ELSE IF branch in sp_Validation_ExecuteRules.';
SELECT  Keyword     = vr.RuleParameter1,
        ActiveRules = COUNT(*),
        Rules       = STUFF((SELECT ', ' + CAST(v2.RuleId AS VARCHAR(10))
                             FROM   dbo.ValidationRules v2
                             WHERE  v2.IsActive = 1
                               AND  v2.ValidationType = 'CUSTOM_LOGIC'
                               AND  v2.RuleParameter1 = vr.RuleParameter1
                             ORDER BY v2.RuleId
                             FOR XML PATH('')), 1, 2, '')
FROM    dbo.ValidationRules vr
WHERE   vr.IsActive = 1 AND vr.ValidationType = 'CUSTOM_LOGIC'
GROUP BY vr.RuleParameter1
ORDER BY vr.RuleParameter1;

PRINT '';
PRINT '--- Re-run 010 to confirm the 50002 entry is gone. Expect no findings from';
PRINT '    rule 27 on current data: every AffiliatedEIN has employees, which is';
PRINT '    the correct result rather than a silent one. ---';
GO
