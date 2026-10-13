/* =============================================================================
   039_tracker_list_employer_scope.sql

   The Tracker employer list (sp_Tracker_GetEmployerList) showed every employer to
   everyone who can open the Tracker. Account Managers and Data Analysts work on their
   own employers everywhere else, so the list now takes the caller's employers:

   - New optional parameter @EmployerIDs (comma-separated employer ids). The web app
     passes it only for roles that do not see every employer; NULL keeps today's
     behaviour, so SuperAdmin / Admin / ACA Director / supervisors are unchanged.
   - A row is shown when the employer, or its primary employer (affiliates), is in
     the list.
   Everything else in the procedure is unchanged.

   Run before deploying the web app that passes @EmployerIDs. Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_Tracker_GetEmployerList]

    -- %% Search %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    @SearchText             NVARCHAR(200)   = NULL,
    @MainEmployerId         INT             = NULL,
    @AffiliateEmployerId    INT             = NULL,
    -- %% Organization %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    @FirmId                 INT             = NULL,
    @BrokerId               INT             = NULL,
    @State                  NVARCHAR(5)     = NULL,
    @FilingYear             NVARCHAR(10)    = NULL,

    -- %% Team %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    @AcctManagerId          INT             = NULL,
    @DataAnalystId          INT             = NULL,
    @SalesRepId             INT             = NULL,

    -- %% Configuration %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    @FundingType            NVARCHAR(100)   = NULL,
    @FormType               NVARCHAR(50)    = NULL,
    @LevelOfComplexity      NVARCHAR(50)    = NULL,
    @IndustryId             INT             = NULL,

    -- %% Flags %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    @IsPriority             BIT             = NULL,
    @NoAutoRenewal          BIT             = NULL,
    @SFTP                   BIT             = NULL,
    @AnnualCOI              BIT             = NULL,

    -- %% Access (039) %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    -- Comma-separated employer ids the caller may see. NULL = no restriction (roles that
    -- see every employer); "-1" or an empty list = nothing.
    @EmployerIDs            NVARCHAR(MAX)   = NULL,

    -- %% Pagination / sorting %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
    @SortColumn             NVARCHAR(50)    = 'EmployerName',
    @SortOrder              NVARCHAR(4)     = 'ASC',
    @PageNumber             INT             = 1,
    @PageSize               INT             = 15

AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Offset INT = (@PageNumber - 1) * @PageSize;

    -- %% Sanitise sort inputs (prevent dynamic SQL injection) %%
    SET @SortColumn = CASE
        WHEN @SortColumn IN ('EmployerName', 'FirmName', 'State', 'City', 'AccountManagerName', 'DataAnalyst') THEN @SortColumn
        ELSE 'EmployerName'
    END;
    SET @SortOrder = CASE WHEN @SortOrder = 'DESC' THEN 'DESC' ELSE 'ASC' END;

    SELECT
        e.id                                                    AS EmployerId,
        e.[name]                                                AS EmployerName,
        e.taxid                                                  AS EIN,
        e.city                                                   AS City,
        e.[state]                                                AS State,
        e.zip                                                     AS Zip,
        f.FirmName                                                AS FirmName,
        CAST(ISNULL(ext.Priority,            0) AS BIT)           AS IsPriority,
        CAST(ISNULL(ext._noAutoRenewal,      0) AS BIT)           AS NoAutoRenewal,
        CAST(ISNULL(ext.SFTP,                0) AS BIT)           AS SFTP,
        CAST(ISNULL(ext._AnnualCOI,          0) AS BIT)           AS AnnualCOI,
        ext._fundingtype                                          AS FundingType,
        ext._FormType                                             AS FormType,
        ext._LevelofComplexity                                    AS LevelOfComplexity,
        ext._planyear                                             AS PlanYear,
        am.Staff_FName + ' ' + am.Staff_LName                     AS AccountManagerName,
        da.Staff_FName + ' ' + da.Staff_LName                     AS DataAnalyst,
        COUNT(*) OVER ()                                          AS TotalCount
    FROM            [dbo].[Employer]                     e
    LEFT JOIN       [dbo].[Tracker_EmployerExtension]    ext  ON e.id = ext.EmployerId
    LEFT JOIN       [dbo].[Tracker_Firm]                 f    ON ext.firmid = f.FirmId
    LEFT JOIN       [dbo].[Tracker_Broker]               b    ON ext.brokerid = b.BrokerId
    LEFT JOIN       [dbo].[StaffAccounts]                am   ON ext.AcctManagerid = am.Staff_ID
    LEFT JOIN       [dbo].[StaffAccounts]                da   ON ext._dataAnalyst = da.Staff_ID
    LEFT JOIN       [dbo].[tbl_Medtracker_SalesRep]      sr   ON ext.srepid = sr.srepid
    WHERE
        -- %% Preserved exactly from original SP %%%%%%%%%%%%%%%%%%%%%%%%%%%%
        e.IsDeleted = 0

        -- %% Access (039): only the caller's employers, and affiliates of them %%
        AND (@EmployerIDs IS NULL
             OR e.id        IN (SELECT TRY_CAST(value AS INT) FROM STRING_SPLIT(@EmployerIDs, ','))
             OR e.companyId IN (SELECT TRY_CAST(value AS INT) FROM STRING_SPLIT(@EmployerIDs, ',')))

        -- %% Existing filters (preserved, converted to static) %
        AND (@SearchText IS NULL OR @SearchText = ''
                  OR e.[name] LIKE '%' + @SearchText + '%'
                  OR e.taxid   LIKE '%' + @SearchText + '%')
        AND (@FirmId           IS NULL OR ext.firmid           = @FirmId)
        AND (@BrokerId         IS NULL OR ext.brokerid         = @BrokerId)
        AND (@FundingType      IS NULL OR @FundingType = ''
                  OR ext._fundingtype = @FundingType)
        AND (@IsPriority       IS NULL OR ext.Priority         = @IsPriority)

        -- %% New filters (all nullable, no effect when NULL) %%%
        AND (@State                    IS NULL OR e.[state]                          = @State)
        AND (@FilingYear                IS NULL OR e.filingYear						 = @FilingYear)
        AND (@AcctManagerId             IS NULL OR ext.AcctManagerid                 = @AcctManagerId)
        AND (@DataAnalystId             IS NULL OR TRY_CAST(ext._dataAnalyst AS INT)  = @DataAnalystId)
        AND (@SalesRepId                IS NULL OR ext.srepid                        = @SalesRepId)
        AND (@FormType                  IS NULL OR ext._FormType                     = @FormType)
        AND (@LevelOfComplexity         IS NULL OR ext._LevelofComplexity            = @LevelOfComplexity)
        AND (@IndustryId                IS NULL OR ext._IndustryId                   = @IndustryId)
        AND (@NoAutoRenewal             IS NULL OR ext._noAutoRenewal                = @NoAutoRenewal)
        AND (@SFTP                      IS NULL OR ext.SFTP                          = @SFTP)
        AND (@AnnualCOI                 IS NULL OR ext._AnnualCOI                    = @AnnualCOI)
        -- If affiliate selected -> show only that affiliate
        -- If main selected (no affiliate) -> show main + all its affiliates
        -- If nothing selected -> show everything (existing companyId logic)
        AND (
            @AffiliateEmployerId IS NOT NULL AND e.id = @AffiliateEmployerId
            OR
            @AffiliateEmployerId IS NULL
            AND @MainEmployerId IS NOT NULL
            AND (e.id = @MainEmployerId OR e.companyId = @MainEmployerId)
            OR
            @AffiliateEmployerId IS NULL
            AND @MainEmployerId IS NULL
            AND (e.companyId IS NULL OR e.companyId = 0)
        )
    ORDER BY
        CASE WHEN @SortColumn = 'EmployerName'       AND @SortOrder = 'ASC'  THEN e.[name]                              END ASC,
        CASE WHEN @SortColumn = 'EmployerName'       AND @SortOrder = 'DESC' THEN e.[name]                              END DESC,
        CASE WHEN @SortColumn = 'FirmName'           AND @SortOrder = 'ASC'  THEN f.FirmName                           END ASC,
        CASE WHEN @SortColumn = 'FirmName'           AND @SortOrder = 'DESC' THEN f.FirmName                           END DESC,
        CASE WHEN @SortColumn = 'State'              AND @SortOrder = 'ASC'  THEN e.[state]                            END ASC,
        CASE WHEN @SortColumn = 'State'              AND @SortOrder = 'DESC' THEN e.[state]                            END DESC,
        CASE WHEN @SortColumn = 'AccountManagerName' AND @SortOrder = 'ASC'  THEN am.Staff_FName + ' ' + am.Staff_LName END ASC,
        CASE WHEN @SortColumn = 'AccountManagerName' AND @SortOrder = 'DESC' THEN am.Staff_FName + ' ' + am.Staff_LName END DESC,
        CASE WHEN @SortColumn = 'DataAnalyst'        AND @SortOrder = 'ASC'  THEN da.Staff_FName + ' ' + da.Staff_LName END ASC,
        CASE WHEN @SortColumn = 'DataAnalyst'      AND @SortOrder = 'DESC' THEN da.Staff_FName + ' ' + da.Staff_LName END DESC,
        e.[name] ASC -- tiebreaker

    OFFSET @Offset ROWS
    FETCH NEXT @PageSize ROWS ONLY;

END
GO
