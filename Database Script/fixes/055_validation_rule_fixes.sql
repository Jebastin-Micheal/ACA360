/* =============================================================================
   055_validation_rule_fixes.sql

   Validation rules that did not do what they say (found while building the
   Dummy Data page):

     1. Rules that look a value up in another staging table (EXISTS_IN_TABLE)
        checked the looked-up column against the wrong table, so their SQL came
        out empty and they never ran: rule 12 (employee's plan exists) and rule
        74 (employee's associated EIN is in the Employer tab). The engine is
        fixed. Rule 74 now runs as its own check, accepting the main employer's
        Primary EIN as well as the affiliates' EINs (an error: other employees
        are kept out of the import). Rule 12 is switched off: rule 135 already
        reports a missing plan.
        A rule whose SQL cannot be built is now written to DB_Errors.
     2. Rule 19 ("Status Active with Term Date") looked for Status = 'Active',
        but Status is 0 or 1, so it never fired. It now warns when an employee
        has a Termination Date but no Status End Date (or one after it).
     3. Affordability (rule 105) matched every banded premium by salary. Age
        bands now match the employee's age, hourly bands the hourly rate and
        years-of-service bands the years since hire (on 1 January of the plan
        year); salary bands are unchanged.
     4. ZIP code against state (rule 107) covered NY, CA, TX, FL and IL by first
        digit. It now uses dbo.ZipPrefixState, every 3-digit ZIP prefix by state
        (with DC, territories and military). A prefix or state not in the table
        is not reported; foreign addresses are skipped.
     5. Auto-correction (sp_Validation_AutoCorrect) changed values without a
        trace. Every change is now kept in dbo.StagingAutoCorrections (before and
        after) and shown on its row as an Information note ("Corrected
        automatically: ... became ..."), through new StagingRowErrors columns
        ColumnName and Detail that the issue lists show. Information notes do not
        affect the import or the file's status. Formatting (upper case, EIN
        hyphen, ZIP leading zero, phone digits) is not reported.

   The procedures are the database's current ones with only these changes.
   Safe to re-run.
   ============================================================================= */
SET NOCOUNT ON;
GO

-- 1. Tables and columns ------------------------------------------------------
IF COL_LENGTH('dbo.StagingRowErrors', 'ColumnName') IS NULL
    ALTER TABLE dbo.StagingRowErrors ADD ColumnName NVARCHAR(128) NULL;
IF COL_LENGTH('dbo.StagingRowErrors', 'Detail') IS NULL
    ALTER TABLE dbo.StagingRowErrors ADD Detail NVARCHAR(500) NULL;
GO

IF OBJECT_ID('dbo.StagingAutoCorrections') IS NULL
BEGIN
    CREATE TABLE dbo.StagingAutoCorrections
    (
        StagingAutoCorrectionId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StagingAutoCorrections PRIMARY KEY,
        FileLogId        INT            NOT NULL,
        StagingTableName NVARCHAR(128)  NOT NULL,
        StagingRowId     BIGINT         NOT NULL,
        ColumnName       NVARCHAR(128)  NOT NULL,
        RuleId           INT            NULL,
        OldValue         NVARCHAR(4000) NULL,
        NewValue         NVARCHAR(4000) NULL,
        CorrectedAt      DATETIME2(0)   NOT NULL CONSTRAINT DF_StagingAutoCorrections_CorrectedAt DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_StagingAutoCorrections_File ON dbo.StagingAutoCorrections (FileLogId, StagingTableName, StagingRowId);
END
GO

IF OBJECT_ID('dbo.ZipPrefixState') IS NULL
    CREATE TABLE dbo.ZipPrefixState
    (
        Prefix CHAR(3) NOT NULL,
        State  CHAR(2) NOT NULL,
        CONSTRAINT PK_ZipPrefixState PRIMARY KEY (Prefix, State)
    );
GO
DECLARE @Zip TABLE (Prefix CHAR(3) NOT NULL, State CHAR(2) NOT NULL, PRIMARY KEY (Prefix, State));
INSERT INTO @Zip (Prefix, State) VALUES
    ('005','NY'), ('006','PR'), ('007','PR'), ('008','VI'), ('009','PR'), ('010','MA'), ('011','MA'), ('012','MA'), ('013','MA'), ('014','MA'),
    ('015','MA'), ('016','MA'), ('017','MA'), ('018','MA'), ('019','MA'), ('020','MA'), ('021','MA'), ('022','MA'), ('023','MA'), ('024','MA'),
    ('025','MA'), ('026','MA'), ('027','MA'), ('028','RI'), ('029','RI'), ('030','NH'), ('031','NH'), ('032','NH'), ('033','NH'), ('034','NH'),
    ('035','NH'), ('036','NH'), ('037','NH'), ('038','NH'), ('039','ME'), ('040','ME'), ('041','ME'), ('042','ME'), ('043','ME'), ('044','ME'),
    ('045','ME'), ('046','ME'), ('047','ME'), ('048','ME'), ('049','ME'), ('050','VT'), ('051','VT'), ('052','VT'), ('053','VT'), ('054','VT'),
    ('055','MA'), ('056','VT'), ('057','VT'), ('058','VT'), ('059','VT'), ('060','CT'), ('061','CT'), ('062','CT'), ('063','CT'), ('064','CT'),
    ('065','CT'), ('066','CT'), ('067','CT'), ('068','CT'), ('069','CT'), ('070','NJ'), ('071','NJ'), ('072','NJ'), ('073','NJ'), ('074','NJ'),
    ('075','NJ'), ('076','NJ'), ('077','NJ'), ('078','NJ'), ('079','NJ'), ('080','NJ'), ('081','NJ'), ('082','NJ'), ('083','NJ'), ('084','NJ'),
    ('085','NJ'), ('086','NJ'), ('087','NJ'), ('088','NJ'), ('089','NJ'), ('090','AE'), ('091','AE'), ('092','AE'), ('093','AE'), ('094','AE'),
    ('095','AE'), ('096','AE'), ('097','AE'), ('098','AE'), ('100','NY'), ('101','NY'), ('102','NY'), ('103','NY'), ('104','NY'), ('105','NY'),
    ('106','NY'), ('107','NY'), ('108','NY'), ('109','NY'), ('110','NY'), ('111','NY'), ('112','NY'), ('113','NY'), ('114','NY'), ('115','NY'),
    ('116','NY'), ('117','NY'), ('118','NY'), ('119','NY'), ('120','NY'), ('121','NY'), ('122','NY'), ('123','NY'), ('124','NY'), ('125','NY'),
    ('126','NY'), ('127','NY'), ('128','NY'), ('129','NY'), ('130','NY'), ('131','NY'), ('132','NY'), ('133','NY'), ('134','NY'), ('135','NY'),
    ('136','NY'), ('137','NY'), ('138','NY'), ('139','NY'), ('140','NY'), ('141','NY'), ('142','NY'), ('143','NY'), ('144','NY'), ('145','NY'),
    ('146','NY'), ('147','NY'), ('148','NY'), ('149','NY'), ('150','PA'), ('151','PA'), ('152','PA'), ('153','PA'), ('154','PA'), ('155','PA'),
    ('156','PA'), ('157','PA'), ('158','PA'), ('159','PA'), ('160','PA'), ('161','PA'), ('162','PA'), ('163','PA'), ('164','PA'), ('165','PA'),
    ('166','PA'), ('167','PA'), ('168','PA'), ('169','PA'), ('170','PA'), ('171','PA'), ('172','PA'), ('173','PA'), ('174','PA'), ('175','PA'),
    ('176','PA'), ('177','PA'), ('178','PA'), ('179','PA'), ('180','PA'), ('181','PA'), ('182','PA'), ('183','PA'), ('184','PA'), ('185','PA'),
    ('186','PA'), ('187','PA'), ('188','PA'), ('189','PA'), ('190','PA'), ('191','PA'), ('192','PA'), ('193','PA'), ('194','PA'), ('195','PA'),
    ('196','PA'), ('197','DE'), ('198','DE'), ('199','DE'), ('200','DC'), ('201','VA'), ('202','DC'), ('203','DC'), ('204','DC'), ('205','DC');
INSERT INTO @Zip (Prefix, State) VALUES
    ('206','MD'), ('207','MD'), ('208','MD'), ('209','MD'), ('210','MD'), ('211','MD'), ('212','MD'), ('213','MD'), ('214','MD'), ('215','MD'),
    ('216','MD'), ('217','MD'), ('218','MD'), ('219','MD'), ('220','VA'), ('221','VA'), ('222','VA'), ('223','VA'), ('224','VA'), ('225','VA'),
    ('226','VA'), ('227','VA'), ('228','VA'), ('229','VA'), ('230','VA'), ('231','VA'), ('232','VA'), ('233','VA'), ('234','VA'), ('235','VA'),
    ('236','VA'), ('237','VA'), ('238','VA'), ('239','VA'), ('240','VA'), ('241','VA'), ('242','VA'), ('243','VA'), ('244','VA'), ('245','VA'),
    ('246','VA'), ('247','WV'), ('248','WV'), ('249','WV'), ('250','WV'), ('251','WV'), ('252','WV'), ('253','WV'), ('254','WV'), ('255','WV'),
    ('256','WV'), ('257','WV'), ('258','WV'), ('259','WV'), ('260','WV'), ('261','WV'), ('262','WV'), ('263','WV'), ('264','WV'), ('265','WV'),
    ('266','WV'), ('267','WV'), ('268','WV'), ('270','NC'), ('271','NC'), ('272','NC'), ('273','NC'), ('274','NC'), ('275','NC'), ('276','NC'),
    ('277','NC'), ('278','NC'), ('279','NC'), ('280','NC'), ('281','NC'), ('282','NC'), ('283','NC'), ('284','NC'), ('285','NC'), ('286','NC'),
    ('287','NC'), ('288','NC'), ('289','NC'), ('290','SC'), ('291','SC'), ('292','SC'), ('293','SC'), ('294','SC'), ('295','SC'), ('296','SC'),
    ('297','SC'), ('298','SC'), ('299','SC'), ('300','GA'), ('301','GA'), ('302','GA'), ('303','GA'), ('304','GA'), ('305','GA'), ('306','GA'),
    ('307','GA'), ('308','GA'), ('309','GA'), ('310','GA'), ('311','GA'), ('312','GA'), ('313','GA'), ('314','GA'), ('315','GA'), ('316','GA'),
    ('317','GA'), ('318','GA'), ('319','GA'), ('320','FL'), ('321','FL'), ('322','FL'), ('323','FL'), ('324','FL'), ('325','FL'), ('326','FL'),
    ('327','FL'), ('328','FL'), ('329','FL'), ('330','FL'), ('331','FL'), ('332','FL'), ('333','FL'), ('334','FL'), ('335','FL'), ('336','FL'),
    ('337','FL'), ('338','FL'), ('339','FL'), ('340','AA'), ('341','FL'), ('342','FL'), ('343','FL'), ('344','FL'), ('345','FL'), ('346','FL'),
    ('347','FL'), ('348','FL'), ('349','FL'), ('350','AL'), ('351','AL'), ('352','AL'), ('353','AL'), ('354','AL'), ('355','AL'), ('356','AL'),
    ('357','AL'), ('358','AL'), ('359','AL'), ('360','AL'), ('361','AL'), ('362','AL'), ('363','AL'), ('364','AL'), ('365','AL'), ('366','AL'),
    ('367','AL'), ('368','AL'), ('369','AL'), ('370','TN'), ('371','TN'), ('372','TN'), ('373','TN'), ('374','TN'), ('375','TN'), ('376','TN'),
    ('377','TN'), ('378','TN'), ('379','TN'), ('380','TN'), ('381','TN'), ('382','TN'), ('383','TN'), ('384','TN'), ('385','TN'), ('386','MS'),
    ('387','MS'), ('388','MS'), ('389','MS'), ('390','MS'), ('391','MS'), ('392','MS'), ('393','MS'), ('394','MS'), ('395','MS'), ('396','MS'),
    ('397','MS'), ('398','GA'), ('399','GA'), ('400','KY'), ('401','KY'), ('402','KY'), ('403','KY'), ('404','KY'), ('405','KY'), ('406','KY');
INSERT INTO @Zip (Prefix, State) VALUES
    ('407','KY'), ('408','KY'), ('409','KY'), ('410','KY'), ('411','KY'), ('412','KY'), ('413','KY'), ('414','KY'), ('415','KY'), ('416','KY'),
    ('417','KY'), ('418','KY'), ('419','KY'), ('420','KY'), ('421','KY'), ('422','KY'), ('423','KY'), ('424','KY'), ('425','KY'), ('426','KY'),
    ('427','KY'), ('430','OH'), ('431','OH'), ('432','OH'), ('433','OH'), ('434','OH'), ('435','OH'), ('436','OH'), ('437','OH'), ('438','OH'),
    ('439','OH'), ('440','OH'), ('441','OH'), ('442','OH'), ('443','OH'), ('444','OH'), ('445','OH'), ('446','OH'), ('447','OH'), ('448','OH'),
    ('449','OH'), ('450','OH'), ('451','OH'), ('452','OH'), ('453','OH'), ('454','OH'), ('455','OH'), ('456','OH'), ('457','OH'), ('458','OH'),
    ('459','OH'), ('460','IN'), ('461','IN'), ('462','IN'), ('463','IN'), ('464','IN'), ('465','IN'), ('466','IN'), ('467','IN'), ('468','IN'),
    ('469','IN'), ('470','IN'), ('471','IN'), ('472','IN'), ('473','IN'), ('474','IN'), ('475','IN'), ('476','IN'), ('477','IN'), ('478','IN'),
    ('479','IN'), ('480','MI'), ('481','MI'), ('482','MI'), ('483','MI'), ('484','MI'), ('485','MI'), ('486','MI'), ('487','MI'), ('488','MI'),
    ('489','MI'), ('490','MI'), ('491','MI'), ('492','MI'), ('493','MI'), ('494','MI'), ('495','MI'), ('496','MI'), ('497','MI'), ('498','MI'),
    ('499','MI'), ('500','IA'), ('501','IA'), ('502','IA'), ('503','IA'), ('504','IA'), ('505','IA'), ('506','IA'), ('507','IA'), ('508','IA'),
    ('509','IA'), ('510','IA'), ('511','IA'), ('512','IA'), ('513','IA'), ('514','IA'), ('515','IA'), ('516','IA'), ('517','IA'), ('518','IA'),
    ('519','IA'), ('520','IA'), ('521','IA'), ('522','IA'), ('523','IA'), ('524','IA'), ('525','IA'), ('526','IA'), ('527','IA'), ('528','IA'),
    ('530','WI'), ('531','WI'), ('532','WI'), ('533','WI'), ('534','WI'), ('535','WI'), ('536','WI'), ('537','WI'), ('538','WI'), ('539','WI'),
    ('540','WI'), ('541','WI'), ('542','WI'), ('543','WI'), ('544','WI'), ('545','WI'), ('546','WI'), ('547','WI'), ('548','WI'), ('549','WI'),
    ('550','MN'), ('551','MN'), ('552','MN'), ('553','MN'), ('554','MN'), ('555','MN'), ('556','MN'), ('557','MN'), ('558','MN'), ('559','MN'),
    ('560','MN'), ('561','MN'), ('562','MN'), ('563','MN'), ('564','MN'), ('565','MN'), ('566','MN'), ('567','MN'), ('569','DC'), ('570','SD'),
    ('571','SD'), ('572','SD'), ('573','SD'), ('574','SD'), ('575','SD'), ('576','SD'), ('577','SD'), ('580','ND'), ('581','ND'), ('582','ND'),
    ('583','ND'), ('584','ND'), ('585','ND'), ('586','ND'), ('587','ND'), ('588','ND'), ('590','MT'), ('591','MT'), ('592','MT'), ('593','MT'),
    ('594','MT'), ('595','MT'), ('596','MT'), ('597','MT'), ('598','MT'), ('599','MT'), ('600','IL'), ('601','IL'), ('602','IL'), ('603','IL'),
    ('604','IL'), ('605','IL'), ('606','IL'), ('607','IL'), ('608','IL'), ('609','IL'), ('610','IL'), ('611','IL'), ('612','IL'), ('613','IL');
INSERT INTO @Zip (Prefix, State) VALUES
    ('614','IL'), ('615','IL'), ('616','IL'), ('617','IL'), ('618','IL'), ('619','IL'), ('620','IL'), ('621','IL'), ('622','IL'), ('623','IL'),
    ('624','IL'), ('625','IL'), ('626','IL'), ('627','IL'), ('628','IL'), ('629','IL'), ('630','MO'), ('631','MO'), ('632','MO'), ('633','MO'),
    ('634','MO'), ('635','MO'), ('636','MO'), ('637','MO'), ('638','MO'), ('639','MO'), ('640','MO'), ('641','MO'), ('642','MO'), ('643','MO'),
    ('644','MO'), ('645','MO'), ('646','MO'), ('647','MO'), ('648','MO'), ('649','MO'), ('650','MO'), ('651','MO'), ('652','MO'), ('653','MO'),
    ('654','MO'), ('655','MO'), ('656','MO'), ('657','MO'), ('658','MO'), ('660','KS'), ('661','KS'), ('662','KS'), ('663','KS'), ('664','KS'),
    ('665','KS'), ('666','KS'), ('667','KS'), ('668','KS'), ('669','KS'), ('670','KS'), ('671','KS'), ('672','KS'), ('673','KS'), ('674','KS'),
    ('675','KS'), ('676','KS'), ('677','KS'), ('678','KS'), ('679','KS'), ('680','NE'), ('681','NE'), ('682','NE'), ('683','NE'), ('684','NE'),
    ('685','NE'), ('686','NE'), ('687','NE'), ('688','NE'), ('689','NE'), ('690','NE'), ('691','NE'), ('692','NE'), ('693','NE'), ('700','LA'),
    ('701','LA'), ('702','LA'), ('703','LA'), ('704','LA'), ('705','LA'), ('706','LA'), ('707','LA'), ('708','LA'), ('709','LA'), ('710','LA'),
    ('711','LA'), ('712','LA'), ('713','LA'), ('714','LA'), ('716','AR'), ('717','AR'), ('718','AR'), ('719','AR'), ('720','AR'), ('721','AR'),
    ('722','AR'), ('723','AR'), ('724','AR'), ('725','AR'), ('726','AR'), ('727','AR'), ('728','AR'), ('729','AR'), ('730','OK'), ('731','OK'),
    ('733','TX'), ('734','OK'), ('735','OK'), ('736','OK'), ('737','OK'), ('738','OK'), ('739','OK'), ('740','OK'), ('741','OK'), ('742','OK'),
    ('743','OK'), ('744','OK'), ('745','OK'), ('746','OK'), ('747','OK'), ('748','OK'), ('749','OK'), ('750','TX'), ('751','TX'), ('752','TX'),
    ('753','TX'), ('754','TX'), ('755','TX'), ('756','TX'), ('757','TX'), ('758','TX'), ('759','TX'), ('760','TX'), ('761','TX'), ('762','TX'),
    ('763','TX'), ('764','TX'), ('765','TX'), ('766','TX'), ('767','TX'), ('768','TX'), ('769','TX'), ('770','TX'), ('771','TX'), ('772','TX'),
    ('773','TX'), ('774','TX'), ('775','TX'), ('776','TX'), ('777','TX'), ('778','TX'), ('779','TX'), ('780','TX'), ('781','TX'), ('782','TX'),
    ('783','TX'), ('784','TX'), ('785','TX'), ('786','TX'), ('787','TX'), ('788','TX'), ('789','TX'), ('790','TX'), ('791','TX'), ('792','TX'),
    ('793','TX'), ('794','TX'), ('795','TX'), ('796','TX'), ('797','TX'), ('798','TX'), ('799','TX'), ('800','CO'), ('801','CO'), ('802','CO'),
    ('803','CO'), ('804','CO'), ('805','CO'), ('806','CO'), ('807','CO'), ('808','CO'), ('809','CO'), ('810','CO'), ('811','CO'), ('812','CO'),
    ('813','CO'), ('814','CO'), ('815','CO'), ('816','CO'), ('820','WY'), ('821','WY'), ('822','WY'), ('823','WY'), ('824','WY'), ('825','WY');
INSERT INTO @Zip (Prefix, State) VALUES
    ('826','WY'), ('827','WY'), ('828','WY'), ('829','WY'), ('830','WY'), ('831','WY'), ('832','ID'), ('833','ID'), ('834','ID'), ('835','ID'),
    ('836','ID'), ('837','ID'), ('838','ID'), ('840','UT'), ('841','UT'), ('842','UT'), ('843','UT'), ('844','UT'), ('845','UT'), ('846','UT'),
    ('847','UT'), ('850','AZ'), ('851','AZ'), ('852','AZ'), ('853','AZ'), ('854','AZ'), ('855','AZ'), ('856','AZ'), ('857','AZ'), ('858','AZ'),
    ('859','AZ'), ('860','AZ'), ('861','AZ'), ('862','AZ'), ('863','AZ'), ('864','AZ'), ('865','AZ'), ('870','NM'), ('871','NM'), ('872','NM'),
    ('873','NM'), ('874','NM'), ('875','NM'), ('876','NM'), ('877','NM'), ('878','NM'), ('879','NM'), ('880','NM'), ('881','NM'), ('882','NM'),
    ('883','NM'), ('884','NM'), ('885','TX'), ('889','NV'), ('890','NV'), ('891','NV'), ('892','NV'), ('893','NV'), ('894','NV'), ('895','NV'),
    ('896','NV'), ('897','NV'), ('898','NV'), ('900','CA'), ('901','CA'), ('902','CA'), ('903','CA'), ('904','CA'), ('905','CA'), ('906','CA'),
    ('907','CA'), ('908','CA'), ('909','CA'), ('910','CA'), ('911','CA'), ('912','CA'), ('913','CA'), ('914','CA'), ('915','CA'), ('916','CA'),
    ('917','CA'), ('918','CA'), ('919','CA'), ('920','CA'), ('921','CA'), ('922','CA'), ('923','CA'), ('924','CA'), ('925','CA'), ('926','CA'),
    ('927','CA'), ('928','CA'), ('929','CA'), ('930','CA'), ('931','CA'), ('932','CA'), ('933','CA'), ('934','CA'), ('935','CA'), ('936','CA'),
    ('937','CA'), ('938','CA'), ('939','CA'), ('940','CA'), ('941','CA'), ('942','CA'), ('943','CA'), ('944','CA'), ('945','CA'), ('946','CA'),
    ('947','CA'), ('948','CA'), ('949','CA'), ('950','CA'), ('951','CA'), ('952','CA'), ('953','CA'), ('954','CA'), ('955','CA'), ('956','CA'),
    ('957','CA'), ('958','CA'), ('959','CA'), ('960','CA'), ('961','CA'), ('962','AP'), ('963','AP'), ('964','AP'), ('965','AP'), ('966','AP'),
    ('967','HI'), ('968','HI'), ('967','AS'), ('969','GU'), ('969','MP'), ('969','PW'), ('969','FM'), ('969','MH'), ('970','OR'), ('971','OR'),
    ('972','OR'), ('973','OR'), ('974','OR'), ('975','OR'), ('976','OR'), ('977','OR'), ('978','OR'), ('979','OR'), ('980','WA'), ('981','WA'),
    ('982','WA'), ('983','WA'), ('984','WA'), ('985','WA'), ('986','WA'), ('987','WA'), ('988','WA'), ('989','WA'), ('990','WA'), ('991','WA'),
    ('992','WA'), ('993','WA'), ('994','WA'), ('995','AK'), ('996','AK'), ('997','AK'), ('998','AK'), ('999','AK');
INSERT INTO dbo.ZipPrefixState (Prefix, State)
SELECT Z.Prefix, Z.State FROM @Zip Z
WHERE NOT EXISTS (SELECT 1 FROM dbo.ZipPrefixState X WHERE X.Prefix = Z.Prefix AND X.State = Z.State);
GO

-- 2. Rules -------------------------------------------------------------------
-- The note auto-correction writes. Not run by the rule engine: it stays inactive.
IF NOT EXISTS (SELECT 1 FROM dbo.ValidationRules WHERE RuleCode = 'AUTO_CORRECTED')
    INSERT INTO dbo.ValidationRules (RuleName, TargetTable, TargetColumn, ValidationType, RuleParameter1, RuleParameter2,
                                     ErrorMessage, IsActive, Severity, IsCorrectable, HelpText, RuleCode, IsAutoCorrect)
    VALUES ('Value Corrected Automatically', 'Staging_Employees', 'Value', 'AUTO_CORRECTED', NULL, NULL,
            N'A value was corrected automatically.', 0, 'Information', 0,
            N'Written by sp_Validation_AutoCorrect (script 055) for each value an auto-correct rule changed; the note on the row shows the value before and after. Not run by the rule engine: leave it inactive.',
            'AUTO_CORRECTED', 0);

-- Rule 12 duplicates rule 135 (PlanName_Must_Exist), which reports a missing plan.
UPDATE dbo.ValidationRules SET IsActive = 0
WHERE  ValidationType = 'EXISTS_IN_TABLE' AND TargetTable = 'Staging_Employees' AND TargetColumn = 'LowestCostPlanOffered' AND IsActive = 1
  AND  EXISTS (SELECT 1 FROM dbo.ValidationRules WHERE RuleParameter1 = 'PlanName_Must_Exist' AND IsActive = 1);

-- Rule 74 compared the associated EIN with the Affiliated EIN column only, so employees under a main
-- employer whose row leaves Affiliated EIN blank (allowed since 048) would all be errors.
UPDATE dbo.ValidationRules
SET    ValidationType = 'CUSTOM_LOGIC', RuleParameter1 = 'AssociatedEIN_In_EmployerTab', RuleParameter2 = NULL
WHERE  TargetTable = 'Staging_Employees' AND TargetColumn = 'EINAssociatedWithEE' AND ValidationType = 'EXISTS_IN_TABLE';

UPDATE dbo.ValidationRules
SET    RuleName = 'Status Open After Termination',
       ErrorMessage = N'The employee has a Termination Date, but the Status End Date is blank or later than it.'
WHERE  RuleParameter1 = 'Status_ActiveWithTermDate';
GO

-- 3. Procedures --------------------------------------------------------------
-- sp_Validation_ExecuteRules
/* ---------------------------------------------------------------- PART 1 ----
   sp_Validation_ExecuteRules - handlers for the eight missing types, repairs for
   the two malformed blocks, and the missing CUSTOM_LOGIC keyword.
   ---------------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE [dbo].[sp_Validation_ExecuteRules] @FileLogId INT AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @RuleId INT, @TargetTable NVARCHAR(128), @TargetColumn NVARCHAR(128), @ValidationType NVARCHAR(50);
    DECLARE @Param1 NVARCHAR(500), @Param2 NVARCHAR(500), @DynamicSQL NVARCHAR(MAX), @PrimaryKeyColumn NVARCHAR(128);

    -- Quoted forms, safe to concatenate as identifiers. The unquoted @TargetTable
    -- is still needed for StagingRowErrors.StagingTableName, which stores the name.
    DECLARE @QTable NVARCHAR(258), @QColumn NVARCHAR(258), @QPk NVARCHAR(258), @QParam2 NVARCHAR(258);
    DECLARE @ShouldExecute BIT, @RejectReason NVARCHAR(400);
    /* F-08: parsed forms for the eight validation types that had no handler.
       19 active rules used them and silently produced nothing. */
    DECLARE @QParam1Col NVARCHAR(258), @QParam1Table NVARCHAR(258);
    DECLARE @QCondColumn NVARCHAR(258), @CondValue NVARCHAR(400), @CommaPos INT;

    DECLARE val_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT RuleId, TargetTable, TargetColumn, ValidationType, RuleParameter1, RuleParameter2
    FROM ValidationRules WHERE IsActive = 1;

    OPEN val_cursor;
    FETCH NEXT FROM val_cursor INTO @RuleId, @TargetTable, @TargetColumn, @ValidationType, @Param1, @Param2;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @Param1 = REPLACE(ISNULL(@Param1, ''), '''', '''''');
        SET @Param2 = REPLACE(ISNULL(@Param2, ''), '''', '''''');
        SET @ShouldExecute = 1;
        SET @RejectReason  = NULL;
        SET @DynamicSQL    = NULL;

        -- Get Primary Key for the table
        SELECT @PrimaryKeyColumn = PrimaryKeyColumn FROM StagingTableMetadata WHERE TableName = @TargetTable;

        /* ---- (a) identifier allow-list -------------------------------------
           TargetTable, TargetColumn and RuleParameter2 reach this procedure from
           a user-editable table and are concatenated into executable SQL. Accept
           only names that resolve to a registered staging table and a real
           column of it. */
        IF @PrimaryKeyColumn IS NULL
            SET @RejectReason = 'TargetTable "' + ISNULL(@TargetTable, '(null)') + '" is not registered in StagingTableMetadata.';
        ELSE IF NOT EXISTS (SELECT 1 FROM sys.columns
                            WHERE object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@TargetTable))
                              AND name = @TargetColumn)
            SET @RejectReason = 'TargetColumn "' + ISNULL(@TargetColumn, '(null)') + '" is not a column of ' + @TargetTable + '.';
        ELSE IF @ValidationType IN ('MAX_LENGTH', 'MIN_LENGTH') AND TRY_CAST(@Param1 AS INT) IS NULL
            SET @RejectReason = @ValidationType + ' requires an integer RuleParameter1; got "' + @Param1 + '".';
        ELSE IF @ValidationType = 'CUSTOM_LOGIC' AND @Param1 = 'COLUMNS_NOT_EQUAL'
                AND NOT EXISTS (SELECT 1 FROM sys.columns
                                WHERE object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@TargetTable))
                                  AND name = @Param2)
            SET @RejectReason = 'COLUMNS_NOT_EQUAL requires RuleParameter2 to be a column of ' + @TargetTable + '; got "' + @Param2 + '".';
        /* ---- F-08: identifier allow-listing for the newly handled types.
           Same principle as above: anything that reaches executable SQL as an
           identifier has to resolve to a real table or column first. */
        ELSE IF @ValidationType IN ('GREATER_THAN_COLUMN', 'REQUIRED_IF_NULL')
                AND NOT EXISTS (SELECT 1 FROM sys.columns
                                WHERE object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@TargetTable))
                                  AND name = @Param1)
            SET @RejectReason = @ValidationType + ' requires RuleParameter1 to be a column of '
                              + @TargetTable + '; got "' + @Param1 + '".';
        ELSE IF @ValidationType = 'EXISTS_IN_TABLE'
                AND NOT EXISTS (SELECT 1 FROM StagingTableMetadata WHERE TableName = @Param1)
            SET @RejectReason = 'EXISTS_IN_TABLE requires RuleParameter1 to be a staging table registered in '
                              + 'StagingTableMetadata; got "' + @Param1 + '".';
        ELSE IF @ValidationType = 'EXISTS_IN_TABLE'
                AND NOT EXISTS (SELECT 1 FROM sys.columns
                                WHERE object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@Param1))
                                  AND name = @Param2)
            SET @RejectReason = 'EXISTS_IN_TABLE requires RuleParameter2 to be a column of '
                              + @Param1 + '; got "' + ISNULL(@Param2, '(null)') + '".';
        ELSE IF @ValidationType = 'REQUIRED_IF_EQUALS'
                AND (CHARINDEX(',', @Param1) <= 1
                  OR NOT EXISTS (SELECT 1 FROM sys.columns
                                 WHERE object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@TargetTable))
                                   AND name = LEFT(@Param1, CHARINDEX(',', @Param1) - 1)))
            SET @RejectReason = 'REQUIRED_IF_EQUALS requires RuleParameter1 as "Column,Value" where Column '
                              + 'belongs to ' + @TargetTable + '; got "' + @Param1 + '".';
        ELSE IF @ValidationType = 'LENGTH_EQUALS' AND TRY_CAST(@Param1 AS INT) IS NULL
            SET @RejectReason = 'LENGTH_EQUALS requires an integer RuleParameter1; got "' + @Param1 + '".';
        ELSE IF @ValidationType IN ('GREATER_THAN_VALUE', 'LESS_THAN_VALUE')
                AND TRY_CAST(@Param1 AS DECIMAL(18,4)) IS NULL
                AND TRY_CAST(@Param1 AS DATE) IS NULL
            SET @RejectReason = @ValidationType + ' requires a numeric or date RuleParameter1; got "'
                              + @Param1 + '".';

        IF @RejectReason IS NOT NULL
        BEGIN
            INSERT INTO dbo.DB_Errors (UserName, ErrorNumber, ErrorState, ErrorSeverity, ErrorLine,
                                       ErrorProcedure, ErrorMessage, ErrorDateTime)
            VALUES (SUSER_SNAME(), 50001, 1, 16, 0, 'sp_Validation_ExecuteRules',
                    'Rule ' + CAST(@RuleId AS VARCHAR(20)) + ' skipped. ' + @RejectReason, GETDATE());

            FETCH NEXT FROM val_cursor INTO @RuleId, @TargetTable, @TargetColumn, @ValidationType, @Param1, @Param2;
            CONTINUE;
        END

        -- (b) safe identifier forms
        SET @QTable  = QUOTENAME(@TargetTable);
        SET @QColumn = QUOTENAME(@TargetColumn);
        SET @QPk     = QUOTENAME(@PrimaryKeyColumn);
        -- 055: for EXISTS_IN_TABLE, RuleParameter2 is a column of the table it looks in
        -- (RuleParameter1). Checked against the target, rules 12 and 74 never ran.
        SET @QParam2 = CASE WHEN @Param2 <> '' AND EXISTS (SELECT 1 FROM sys.columns
                                                           WHERE object_id = OBJECT_ID(N'dbo.' + QUOTENAME(CASE WHEN @ValidationType = 'EXISTS_IN_TABLE' THEN @Param1 ELSE @TargetTable END))
                                                             AND name = @Param2)
                            THEN QUOTENAME(@Param2) ELSE NULL END;
        -- (b2) F-08 derived identifier forms, all allow-listed above.
        SET @QParam1Col = CASE WHEN EXISTS (SELECT 1 FROM sys.columns
                                            WHERE object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@TargetTable))
                                              AND name = @Param1)
                               THEN QUOTENAME(@Param1) ELSE NULL END;
        SET @QParam1Table = CASE WHEN EXISTS (SELECT 1 FROM StagingTableMetadata WHERE TableName = @Param1)
                                 THEN QUOTENAME(@Param1) ELSE NULL END;
        SET @CommaPos    = CHARINDEX(',', @Param1);
        SET @QCondColumn = CASE WHEN @CommaPos > 1
                                 AND EXISTS (SELECT 1 FROM sys.columns
                                             WHERE object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@TargetTable))
                                               AND name = LEFT(@Param1, @CommaPos - 1))
                                THEN QUOTENAME(LEFT(@Param1, @CommaPos - 1)) ELSE NULL END;
        SET @CondValue   = CASE WHEN @CommaPos > 0
                                THEN LTRIM(RTRIM(SUBSTRING(@Param1, @CommaPos + 1, 400))) ELSE NULL END;

        -- Default Template
        SET @DynamicSQL = N'INSERT INTO StagingRowErrors (FileLogId, StagingTableName, StagingRowId, RuleId)
            SELECT @FileLogId, ''' + @TargetTable + ''', S.' + @QPk + ', @RuleId
            FROM ' + @QTable + ' AS S WHERE S.FileLogId = @FileLogId AND ';

        -- Rule Logic Construction
        IF @ValidationType = 'REQUIRED' SET @DynamicSQL = @DynamicSQL + 'ISNULL(S.' + @QColumn + ', '''') = ''''';
        ELSE IF @ValidationType = 'IS_NUMERIC' SET @DynamicSQL = @DynamicSQL + 'ISNULL(S.' + @QColumn + ', '''') <> '''' AND TRY_CAST(S.' + @QColumn + ' AS DECIMAL(18,2)) IS NULL';
        ELSE IF @ValidationType = 'IS_DATE' SET @DynamicSQL = @DynamicSQL + 'ISNULL(S.' + @QColumn + ', '''') <> '''' AND TRY_CAST(S.' + @QColumn + ' AS DATE) IS NULL';
        ELSE IF @ValidationType = 'MAX_LENGTH' SET @DynamicSQL = @DynamicSQL + 'ISNULL(S.' + @QColumn + ', '''') <> '''' AND LEN(S.' + @QColumn + ') > ' + CAST(TRY_CAST(@Param1 AS INT) AS NVARCHAR(20));
        ELSE IF @ValidationType = 'MIN_LENGTH' SET @DynamicSQL = @DynamicSQL + 'ISNULL(S.' + @QColumn + ', '''') <> '''' AND LEN(S.' + @QColumn + ') < ' + CAST(TRY_CAST(@Param1 AS INT) AS NVARCHAR(20));
        ELSE IF @ValidationType = 'REGEX_MATCH' SET @DynamicSQL = @DynamicSQL + 'ISNULL(S.' + @QColumn + ', '''') <> '''' AND S.' + @QColumn + ' NOT LIKE ''' + @Param1 + '''';
        ELSE IF @ValidationType = 'REGEX_CONTAINS' SET @DynamicSQL = @DynamicSQL + 'ISNULL(S.' + @QColumn + ', '''') <> '''' AND S.' + @QColumn + ' LIKE ''' + @Param1 + '''';
        ELSE IF @ValidationType = 'IN_LIST' SET @DynamicSQL = @DynamicSQL + 'ISNULL(S.' + @QColumn + ', '''') <> '''' AND S.' + @QColumn + ' NOT IN (SELECT value FROM STRING_SPLIT(''' + @Param1 + ''', '',''))';


        /* ==== F-08: handlers for the eight types that previously fell through ====
           Each was already seeded and active, so the file was reported clean while
           these checks produced nothing. Fifteen of the nineteen are Error
           severity, which is what let such a file reach approval. */

        -- Target must not be earlier than the column named in RuleParameter1.
        -- Dates and numbers both, decided per row: all five seeded rules compare
        -- dates, but the type does not promise that.
        ELSE IF @ValidationType = 'GREATER_THAN_COLUMN'
            SET @DynamicSQL = @DynamicSQL +
                'ISNULL(S.' + @QColumn + ', '''') <> '''' AND ISNULL(S.' + @QParam1Col + ', '''') <> '''' AND ((' +
                'TRY_CAST(S.' + @QColumn + ' AS DATE) IS NOT NULL AND TRY_CAST(S.' + @QParam1Col + ' AS DATE) IS NOT NULL ' +
                'AND TRY_CAST(S.' + @QColumn + ' AS DATE) < TRY_CAST(S.' + @QParam1Col + ' AS DATE)) OR (' +
                'TRY_CAST(S.' + @QColumn + ' AS DATE) IS NULL ' +
                'AND TRY_CAST(S.' + @QColumn + ' AS DECIMAL(18,4)) IS NOT NULL ' +
                'AND TRY_CAST(S.' + @QParam1Col + ' AS DECIMAL(18,4)) IS NOT NULL ' +
                'AND TRY_CAST(S.' + @QColumn + ' AS DECIMAL(18,4)) < TRY_CAST(S.' + @QParam1Col + ' AS DECIMAL(18,4))))';

        -- Target value must appear in RuleParameter2 of the table named in
        -- RuleParameter1, within the same upload.
        ELSE IF @ValidationType = 'EXISTS_IN_TABLE'
            SET @DynamicSQL = @DynamicSQL +
                'ISNULL(S.' + @QColumn + ', '''') <> '''' AND NOT EXISTS (SELECT 1 FROM ' + @QParam1Table + ' X ' +
                'WHERE X.FileLogId = S.FileLogId ' +
                'AND LTRIM(RTRIM(ISNULL(X.' + @QParam2 + ', ''''))) = LTRIM(RTRIM(S.' + @QColumn + ')))';

        -- RuleParameter1 is "Column,Value": target is required when that column
        -- holds that value. Compared case-insensitively and trimmed, because the
        -- values come from a spreadsheet.
        ELSE IF @ValidationType = 'REQUIRED_IF_EQUALS'
            SET @DynamicSQL = @DynamicSQL +
                'UPPER(LTRIM(RTRIM(ISNULL(S.' + @QCondColumn + ', '''')))) = UPPER(''' + @CondValue + ''') ' +
                'AND ISNULL(LTRIM(RTRIM(S.' + @QColumn + ')), '''') = ''''';

        -- Target is required when the column named in RuleParameter1 is empty.
        -- The seeded rule reads "provide either an SSN or a date of birth", so
        -- the error is both being absent.
        ELSE IF @ValidationType = 'REQUIRED_IF_NULL'
            SET @DynamicSQL = @DynamicSQL +
                'ISNULL(LTRIM(RTRIM(S.' + @QParam1Col + ')), '''') = '''' ' +
                'AND ISNULL(LTRIM(RTRIM(S.' + @QColumn + ')), '''') = ''''';

        -- Digits only: the seeded SSN rule states that dashes are ignored.
        ELSE IF @ValidationType = 'LENGTH_EQUALS'
            SET @DynamicSQL = @DynamicSQL +
                'ISNULL(S.' + @QColumn + ', '''') <> '''' AND LEN(REPLACE(REPLACE(REPLACE(S.' + @QColumn +
                ', ''-'', ''''), '' '', ''''), CHAR(9), '''')) <> ' + CAST(TRY_CAST(@Param1 AS INT) AS NVARCHAR(20));

        -- Numeric or date threshold, whichever RuleParameter1 parses as.
        ELSE IF @ValidationType = 'GREATER_THAN_VALUE'
            SET @DynamicSQL = @DynamicSQL + 'ISNULL(S.' + @QColumn + ', '''') <> '''' AND ' +
                CASE WHEN TRY_CAST(@Param1 AS DECIMAL(18,4)) IS NOT NULL
                     THEN 'TRY_CAST(S.' + @QColumn + ' AS DECIMAL(18,4)) > ' + @Param1
                     ELSE 'TRY_CAST(S.' + @QColumn + ' AS DATE) > ''' + @Param1 + '''' END;

        ELSE IF @ValidationType = 'LESS_THAN_VALUE'
            SET @DynamicSQL = @DynamicSQL + 'ISNULL(S.' + @QColumn + ', '''') <> '''' AND ' +
                CASE WHEN TRY_CAST(@Param1 AS DECIMAL(18,4)) IS NOT NULL
                     THEN 'TRY_CAST(S.' + @QColumn + ' AS DECIMAL(18,4)) < ' + @Param1
                     ELSE 'TRY_CAST(S.' + @QColumn + ' AS DATE) < ''' + @Param1 + '''' END;

        -- Informational: reports that the column is absent. The seeded rule flags
        -- dependents identified by date of birth rather than SSN.
        ELSE IF @ValidationType = 'IS_NULL'
            SET @DynamicSQL = @DynamicSQL + 'ISNULL(LTRIM(RTRIM(S.' + @QColumn + ')), '''') = ''''';
        ELSE IF @ValidationType = 'IS_UNIQUE' BEGIN
            SET @DynamicSQL = N';WITH Duplicates AS (
                SELECT ' + @QPk + ',
                COUNT(1) OVER(PARTITION BY ' + @QColumn + ') AS total_cnt
                FROM ' + @QTable + '
                WHERE FileLogId = @FileLogId AND ISNULL(' + @QColumn + ', '''') <> ''''
            )
            INSERT INTO StagingRowErrors (FileLogId, StagingTableName, StagingRowId, RuleId)
            SELECT @FileLogId, ''' + @TargetTable + ''', ' + @QPk + ', @RuleId
            FROM Duplicates
            WHERE total_cnt > 1';
        END
        -- Custom Logic Blocks
        ELSE IF @ValidationType = 'CUSTOM_LOGIC' BEGIN
             IF @Param1 = 'Status_ActiveWithTermDate'
                SET @DynamicSQL = @DynamicSQL + 'TRY_CAST(S.TerminationDate AS DATE) IS NOT NULL AND (ISNULL(S.StatusEndDate, '''') = '''' OR TRY_CAST(S.StatusEndDate AS DATE) > TRY_CAST(S.TerminationDate AS DATE))';   -- 055: Status is 0/1, never ''Active''
             ELSE IF @Param1 = 'Affiliates_MustHaveEmployees' SET @DynamicSQL = @DynamicSQL + 'NOT EXISTS (SELECT 1 FROM Staging_Employees E WHERE E.FileLogId = S.FileLogId AND E.EINAssociatedWithEE = S.AffiliatedEIN)';
             ELSE IF @Param1 = 'AssociatedEIN_In_EmployerTab'   -- 055: rule 74; the main employer's own EIN counts too
                SET @DynamicSQL = @DynamicSQL + 'ISNULL(S.EINAssociatedWithEE, '''') <> '''' AND NOT EXISTS (SELECT 1 FROM Staging_Employers X WHERE X.FileLogId = S.FileLogId AND REPLACE(LTRIM(RTRIM(S.EINAssociatedWithEE)), ''-'', '''') IN (REPLACE(LTRIM(RTRIM(ISNULL(X.AffiliatedEIN, ''''))), ''-'', ''''), REPLACE(LTRIM(RTRIM(ISNULL(X.PrimaryEIN, ''''))), ''-'', '''')))';
             ELSE IF @Param1 = 'SSN_InvalidStructure' SET @DynamicSQL = @DynamicSQL + '(S.' + @QColumn + ' LIKE ''000%'' OR S.' + @QColumn + ' LIKE ''666%'' OR S.' + @QColumn + ' LIKE ''9%'')';
             ELSE IF @Param1 = 'Premium_PercentIfBanded' SET @DynamicSQL = @DynamicSQL + 'S.[BandingType] = ''MONTHLY WITH PERCENT'' AND CHARINDEX(''%'', S.' + @QColumn + ') = 0';
             ELSE IF @Param1 = 'Dependent_On_FullyInsured_Plan' SET @DynamicSQL = @DynamicSQL + 'EXISTS (SELECT 1 FROM Staging_Employees E JOIN Staging_Plans P ON E.LowestCostPlanOffered = P.PlanName AND E.FileLogId = P.FileLogId WHERE E.EmployeeSSN = S.EmployeeSSN AND E.FileLogId = S.FileLogId AND P.FundingType = ''1'')';

             -- [NEW RULE ADDED HERE FOR REHIRES / OVERLAPPING SSN DATES]
             ELSE IF @Param1 = 'Check_Valid_Rehire_Timeline'
             SET @DynamicSQL = @DynamicSQL + 'EXISTS (SELECT 1 FROM ' + @QTable + ' S2 WHERE S2.FileLogId = S.FileLogId AND S2.EmployeeSSN = S.EmployeeSSN AND S2.' + @QPk + ' <> S.' + @QPk + ' AND ISDATE(S.StatusStartDate)=1 AND ISDATE(S2.StatusStartDate)=1 AND CAST(S.StatusStartDate AS DATE) <= CAST(ISNULL(S2.StatusEndDate, ''2099-12-31'') AS DATE) AND CAST(ISNULL(S.StatusEndDate, ''2099-12-31'') AS DATE) >= CAST(S2.StatusStartDate AS DATE))';

             ELSE IF @Param1 = 'DateEligible_Matches_WaitingPeriod'
             SET @DynamicSQL = @DynamicSQL + 'EXISTS (
                 SELECT 1 FROM Staging_Plans P
                 WHERE P.FileLogId = S.FileLogId
                 AND P.PlanName = S.LowestCostPlanOffered
                 AND ISDATE(S.DateEligibleForCoverage) = 1
                 AND ISDATE(S.HireDate) = 1
                 -- EXEMPTION 1: Ignore Legacy Employees (Hired > 1 year before Eligibility)
                 AND DATEDIFF(DAY, CAST(S.HireDate AS DATE), CAST(S.DateEligibleForCoverage AS DATE)) < 365
                 -- EXEMPTION 2: Ignore January 1st rollovers
                 AND NOT (MONTH(CAST(S.DateEligibleForCoverage AS DATE)) = 1 AND DAY(CAST(S.DateEligibleForCoverage AS DATE)) = 1)
                 -- VIOLATIONS
                 AND (
                     DATEDIFF(DAY, CAST(S.HireDate AS DATE), CAST(S.DateEligibleForCoverage AS DATE)) > 90
                     OR
                     DATEDIFF(DAY, CAST(S.HireDate AS DATE), CAST(S.DateEligibleForCoverage AS DATE)) > (ISNULL(TRY_CAST(P.WaitingPeriodNumberOfDays AS INT), 0) + 31)
                 )
             )';
             ELSE IF @Param1 = 'COLUMNS_NOT_EQUAL' SET @DynamicSQL = @DynamicSQL + 'S.' + @QColumn + ' = S.' + @QParam2;
             ELSE IF @Param1 = 'SSN_StartsWith9' SET @DynamicSQL = @DynamicSQL + 'S.' + @QColumn + ' LIKE ''9%''';
             ELSE IF @Param1 = 'CheckZipStateMismatch'   -- 055: every state, from dbo.ZipPrefixState (was NY, CA, TX, FL and IL by first digit)
                SET @DynamicSQL = @DynamicSQL + 'ISNULL(S.ForeignAddress, ''0'') NOT IN (''1'', ''Y'', ''YES'', ''TRUE'') AND LEN(LTRIM(S.Zip)) >= 5 AND LEFT(LTRIM(S.Zip), 3) NOT LIKE ''%[^0-9]%'' AND EXISTS (SELECT 1 FROM dbo.ZipPrefixState Z WHERE Z.Prefix = LEFT(LTRIM(S.Zip), 3)) AND EXISTS (SELECT 1 FROM dbo.ZipPrefixState Z WHERE Z.State = UPPER(LTRIM(RTRIM(S.StateOrProvince)))) AND NOT EXISTS (SELECT 1 FROM dbo.ZipPrefixState Z WHERE Z.Prefix = LEFT(LTRIM(S.Zip), 3) AND Z.State = UPPER(LTRIM(RTRIM(S.StateOrProvince))))';
             ELSE IF @Param1 = 'CheckDateOverlap' SET @DynamicSQL = N'INSERT INTO StagingRowErrors (FileLogId, StagingTableName, StagingRowId, RuleId) SELECT @FileLogId, ''' + @TargetTable + ''', S1.' + @QPk + ', @RuleId FROM ' + @QTable + ' S1 WHERE S1.FileLogId = @FileLogId AND EXISTS (SELECT 1 FROM ' + @QTable + ' S2 WHERE S2.FileLogId = S1.FileLogId AND S2.EmployeeSSN = S1.EmployeeSSN AND S2.' + @QPk + ' <> S1.' + @QPk + ' AND ISDATE(S1.CoverageStartDate)=1 AND ISDATE(S2.CoverageStartDate)=1 AND CAST(S1.CoverageStartDate AS DATE) <= CAST(ISNULL(S2.CoverageEndDate, ''2099-12-31'') AS DATE) AND CAST(ISNULL(S1.CoverageEndDate, ''2099-12-31'') AS DATE) >= CAST(S2.CoverageStartDate AS DATE))';

             ELSE IF @Param1 = 'DepStart_Before_EmpStart'
                SET @DynamicSQL = N'
                    INSERT INTO StagingRowErrors (FileLogId, StagingTableName, StagingRowId, RuleId)
                    SELECT @FileLogId, ''' + @TargetTable + ''', D.' + @QPk + ', @RuleId
                    FROM ' + @QTable + ' D
                    CROSS APPLY (
                        -- An employee can hold several coverage rows (EIN transfer,
                        -- rehire, status change). Their coverage begins at the
                        -- earliest of them, not at whichever row the join reaches.
                        SELECT EarliestStart = MIN(CAST(E.CoverageStartDate AS DATE))
                        FROM   Staging_Employees E
                        WHERE  E.FileLogId  = D.FileLogId
                          AND  E.EmployeeSSN = D.EmployeeSSN
                          AND  ISDATE(E.CoverageStartDate) = 1
                    ) X
                    WHERE D.FileLogId = @FileLogId
                      AND ISDATE(D.DependentCoverageStartDate) = 1
                      AND X.EarliestStart IS NOT NULL
                      AND CAST(D.DependentCoverageStartDate AS DATE) < X.EarliestStart';

            ELSE IF @Param1 = 'DepEnd_After_EmpEnd'
                SET @DynamicSQL = N'
                    INSERT INTO StagingRowErrors (FileLogId, StagingTableName, StagingRowId, RuleId)
                    SELECT @FileLogId, ''' + @TargetTable + ''', D.' + @QPk + ', @RuleId
                    FROM ' + @QTable + ' D
                    CROSS APPLY (
                        SELECT LatestEnd = MAX(CAST(E.CoverageEndDate AS DATE)),
                               -- A blank end date means coverage is still open, so
                               -- nothing can fall after it.
                               OpenEnded = MAX(CASE WHEN ISNULL(LTRIM(RTRIM(E.CoverageEndDate)), '''') = ''''
                                                    THEN 1 ELSE 0 END)
                        FROM   Staging_Employees E
                        WHERE  E.FileLogId  = D.FileLogId
                          AND  E.EmployeeSSN = D.EmployeeSSN
                          AND  ISNULL(LTRIM(RTRIM(E.CoverageStartDate)), '''') <> ''''
                    ) X
                    WHERE D.FileLogId = @FileLogId
                      AND ISDATE(D.DependentCoverageEndDate) = 1
                      AND X.OpenEnded = 0
                      AND X.LatestEnd IS NOT NULL
                      AND CAST(D.DependentCoverageEndDate AS DATE) > X.LatestEnd';

        
		ELSE IF @Param1 = 'CheckAffordability'
                SET @DynamicSQL = N'
                    -- Affordability percentage comes from FilingYear for the file''s
                    -- plan year, not a literal. 9.12% was the 2023 figure.
                    DECLARE @Pct DECIMAL(10,5) = (
                        SELECT fy.fpgPercent
                        FROM   dbo.UploadedFileLog fl
                        JOIN   dbo.FilingYear      fy ON fy.filingYear = fl.PlanYear
                        WHERE  fl.FileLogId = @FileLogId);

                    IF @Pct IS NULL SET @Pct = 0.0902;   -- last known good, if PlanYear is unset

                    -- 055: ages and years of service for age / years-of-service bands, on 1 January of the plan year.
                    DECLARE @Ref DATE = DATEFROMPARTS(ISNULL((SELECT PlanYear FROM dbo.UploadedFileLog WHERE FileLogId = @FileLogId), YEAR(GETDATE())), 1, 1);

                    INSERT INTO StagingRowErrors (FileLogId, StagingTableName, StagingRowId, RuleId)
                    SELECT DISTINCT @FileLogId, ''Staging_Employees'', S.StagingEmployeeId, @RuleId
                    FROM   Staging_Employees S
                    CROSS APPLY (SELECT TRY_CAST(S.EmployeeBirthdate AS DATE) AS Birth, TRY_CAST(S.HireDate AS DATE) AS Hired) D
                    CROSS APPLY (SELECT CAST(DATEDIFF(YEAR, D.Birth, @Ref) - CASE WHEN DATEADD(YEAR, DATEDIFF(YEAR, D.Birth, @Ref), D.Birth) > @Ref THEN 1 ELSE 0 END AS DECIMAL(18,2)) AS AgeAtStart,
                                        CAST(DATEDIFF(YEAR, D.Hired, @Ref) - CASE WHEN DATEADD(YEAR, DATEDIFF(YEAR, D.Hired, @Ref), D.Hired) > @Ref THEN 1 ELSE 0 END AS DECIMAL(18,2)) AS YearsAtStart) X
                    JOIN   Staging_Premiums  P
                           ON  P.FileLogId = S.FileLogId
                           AND P.PlanName  = S.LowestCostPlanOffered
                           -- Match the employee to the band that actually applies to
                           -- them. Without this, a salary-banded plan tests every
                           -- employee against every band.
                           AND (
                                 UPPER(LTRIM(RTRIM(ISNULL(P.BandingType, '''')))) NOT IN
                                     (''AGE'', ''SALARY'', ''HOURLY'', ''YEARS OF SERVICE'')
                                 OR (
                                     TRY_CAST(P.[Start] AS DECIMAL(18,2)) IS NOT NULL
                                     -- 055: was the salary for every kind of band; ages, hourly rates and years of service are not salaries.
                                     AND (CASE UPPER(LTRIM(RTRIM(P.BandingType)))
                                               WHEN ''SALARY'' THEN TRY_CAST(S.W2IncomeOrAnnualSalary AS DECIMAL(18,2))
                                               WHEN ''HOURLY'' THEN TRY_CAST(S.HourlyRate AS DECIMAL(18,2))
                                               WHEN ''AGE'' THEN X.AgeAtStart
                                               WHEN ''YEARS OF SERVICE'' THEN X.YearsAtStart END)
                                         BETWEEN TRY_CAST(P.[Start] AS DECIMAL(18,2))
                                             AND TRY_CAST(P.[End]   AS DECIMAL(18,2))
                                 )
                               )
                    WHERE  S.FileLogId = @FileLogId
                      AND  TRY_CAST(S.W2IncomeOrAnnualSalary AS DECIMAL(18,2)) > 0
                      AND  TRY_CAST(P.EEMonthlyContribution AS DECIMAL(18,2)) IS NOT NULL
                      AND  (
                            CASE
                                -- Percent-banded: the contribution IS the percentage.
                                WHEN UPPER(LTRIM(RTRIM(ISNULL(P.BandingType, '''')))) = ''MONTHLY WITH PERCENT''
                                     THEN CASE WHEN TRY_CAST(P.EEMonthlyContribution AS DECIMAL(18,2)) / 100.0 > @Pct
                                               THEN 1 ELSE 0 END
                                -- Everything else: annualise the dollar contribution.
                                ELSE CASE WHEN (TRY_CAST(P.EEMonthlyContribution AS DECIMAL(18,2)) * 12)
                                             / TRY_CAST(S.W2IncomeOrAnnualSalary AS DECIMAL(18,2)) > @Pct
                                          THEN 1 ELSE 0 END
                            END
                           ) = 1';
             ELSE IF @Param1 = 'CheckCrossEmployeeDependents' SET @DynamicSQL = N';WITH DepCounts AS (SELECT DependentSSN, COUNT(*) as Cnt FROM Staging_Dependents WHERE FileLogId = @FileLogId AND ISNULL(DependentSSN, '''') <> '''' GROUP BY DependentSSN) INSERT INTO StagingRowErrors (FileLogId, StagingTableName, StagingRowId, RuleId) SELECT @FileLogId, ''' + @TargetTable + ''', S.StagingDependentId, @RuleId FROM Staging_Dependents S JOIN DepCounts D ON S.DependentSSN = D.DependentSSN WHERE S.FileLogId = @FileLogId AND D.Cnt > 1';
             ELSE IF @Param1 = 'CheckSelfFundedDependents'
                /* F-08: the literals here were double-escaped, as if this block had
                   been copied out of a nested dynamic-SQL context. It emitted
                   FundingType = ''2'' -- an empty string, a stray 2, another empty
                   string -- which is a syntax error, so every run of this rule threw
                   and was swallowed by the per-rule CATCH below. Single-escaped now. */
                SET @DynamicSQL = @DynamicSQL +
                    'S.[FundingType] = ''2'' ' +
                    -- COBRA and Retiree plans are never a LowestCostPlanOffered, so
                    -- the NOT EXISTS below is always true for them.
                    'AND UPPER(LTRIM(RTRIM(ISNULL(S.PlanType, '''')))) = ''MEDICAL'' ' +
                    'AND NOT EXISTS (SELECT 1 FROM Staging_Employees E ' +
                    '    JOIN Staging_Dependents D ON E.EmployeeSSN = D.EmployeeSSN AND E.FileLogId = D.FileLogId ' +
                    '    WHERE E.LowestCostPlanOffered = S.PlanName AND E.FileLogId = S.FileLogId)';
             ELSE IF @Param1 = 'Cannot_Be_Negative'
                SET @DynamicSQL = @DynamicSQL + 'ISNUMERIC(S.' + @QColumn + ') = 1 AND CAST(S.' + @QColumn + ' AS DECIMAL(18,2)) < 0';

             ELSE IF @Param1 = 'PlanName_Must_Exist'
                SET @DynamicSQL = @DynamicSQL + 'ISNULL(S.' + @QColumn + ', '''') <> '''' AND NOT EXISTS (SELECT 1 FROM Staging_Plans P WHERE P.FileLogId = S.FileLogId AND P.PlanName = S.' + @QColumn + ')';

             ELSE IF @Param1 = 'Start_After_End'
                SET @DynamicSQL = @DynamicSQL + 'ISDATE(S.StartDate) = 1 AND ISDATE(S.EndDate) = 1 AND CAST(S.StartDate AS DATE) > CAST(S.EndDate AS DATE)';

             ELSE IF @Param1 = 'Emp_Start_After_End'
                SET @DynamicSQL = @DynamicSQL + 'ISDATE(S.CoverageStartDate) = 1 AND ISDATE(S.CoverageEndDate) = 1 AND CAST(S.CoverageStartDate AS DATE) > CAST(S.CoverageEndDate AS DATE)';

             ELSE IF @Param1 = 'Month_1_to_12'
                SET @DynamicSQL = @DynamicSQL + '(ISNUMERIC(S.' + @QColumn + ') = 0 OR CAST(S.' + @QColumn + ' AS INT) NOT BETWEEN 1 AND 12)';

             ELSE IF @Param1 = 'Valid_US_ZIP'
                SET @DynamicSQL = @DynamicSQL + '(ISNULL(S.Country, '''') = ''USA'' OR ISNULL(S.Country, '''') = '''') AND S.' + @QColumn + ' NOT LIKE ''[0-9][0-9][0-9][0-9][0-9]'' AND S.' + @QColumn + ' NOT LIKE ''[0-9][0-9][0-9][0-9][0-9]-[0-9][0-9][0-9][0-9]''';

             ELSE IF @Param1 = 'CheckDependentAge26'
                /* F-08: this had the same double-escaping fault as
                   CheckSelfFundedDependents, and additionally emitted the literal
                   text  S.' + @QColumn + '  because the concatenation was inside the
                   string rather than around it. Both fixed. The column is
                   DependentBirthdate, and the test is age at 31 December of the
                   plan year, which is how the age-26 rule is applied. */
                SET @DynamicSQL = @DynamicSQL +
                    'ISNULL(LTRIM(RTRIM(S.Relationship)), '''') NOT IN ('''', ''Spouse'', ''Domestic Partner'') ' +
                    'AND TRY_CAST(S.' + @QColumn + ' AS DATE) IS NOT NULL ' +
                    'AND DATEDIFF(YEAR, TRY_CAST(S.' + @QColumn + ' AS DATE), ' +
                    '    DATEFROMPARTS(ISNULL((SELECT PlanYear FROM UploadedFileLog WHERE FileLogId = @FileLogId), YEAR(GETDATE())), 12, 31)) >= 26';

             ELSE IF @Param1 = 'DependentCoverage_Within_EmployeeCoverage'
                /* F-08: no handler existed for this keyword, so the rule was
                   skipped. Complements DepStart_Before_EmpStart and
                   DepEnd_After_EmpEnd by catching the case those two cannot: a
                   dependent carrying coverage for an employee who has none. */
                SET @DynamicSQL = @DynamicSQL +
                    'ISNULL(LTRIM(RTRIM(S.DependentCoverageStartDate)), '''') <> '''' ' +
                    'AND NOT EXISTS (SELECT 1 FROM Staging_Employees E ' +
                    '    WHERE E.FileLogId = S.FileLogId AND E.EmployeeSSN = S.EmployeeSSN ' +
                    '      AND ISNULL(LTRIM(RTRIM(E.CoverageStartDate)), '''') <> '''')';
             ELSE
             BEGIN
                -- (f) unrecognised CUSTOM_LOGIC keyword: does not fire, but say so
                SET @ShouldExecute = 0;
                INSERT INTO dbo.DB_Errors (UserName, ErrorNumber, ErrorState, ErrorSeverity, ErrorLine,
                                           ErrorProcedure, ErrorMessage, ErrorDateTime)
                VALUES (SUSER_SNAME(), 50002, 1, 10, 0, 'sp_Validation_ExecuteRules',
                        'Rule ' + CAST(@RuleId AS VARCHAR(20)) + ' did not run: no handler for CUSTOM_LOGIC keyword "' + @Param1 + '".', GETDATE());
             END
        END
        ELSE
        BEGIN
            -- (f) unrecognised ValidationType. 19 seeded rules land here today.
            SET @ShouldExecute = 0;
            INSERT INTO dbo.DB_Errors (UserName, ErrorNumber, ErrorState, ErrorSeverity, ErrorLine,
                                       ErrorProcedure, ErrorMessage, ErrorDateTime)
            VALUES (SUSER_SNAME(), 50003, 1, 10, 0, 'sp_Validation_ExecuteRules',
                    'Rule ' + CAST(@RuleId AS VARCHAR(20)) + ' did not run: no handler for ValidationType "' + @ValidationType + '".', GETDATE());
        END

        -- 055: a rule whose SQL came out empty (a parameter naming no column) used to be skipped silently.
        IF @ShouldExecute = 1 AND @DynamicSQL IS NULL
            INSERT INTO dbo.DB_Errors (UserName, ErrorNumber, ErrorState, ErrorSeverity, ErrorLine,
                                       ErrorProcedure, ErrorMessage, ErrorDateTime)
            VALUES (SUSER_SNAME(), 50004, 1, 10, 0, 'sp_Validation_ExecuteRules',
                    'Rule ' + CAST(@RuleId AS VARCHAR(20)) + ' did not run: its SQL could not be built (check its parameters).', GETDATE());
        IF @ShouldExecute = 1 AND @DynamicSQL IS NOT NULL
        BEGIN
            -- Critical optimization: Force Single Core for INSERTs to avoid CXSYNC_PORT
            SET @DynamicSQL = @DynamicSQL + ' OPTION (MAXDOP 1)';

            -- (g) per-rule isolation. Remove this TRY/CATCH to restore fail-fast.
            BEGIN TRY
                EXEC sp_executesql @DynamicSQL, N'@FileLogId INT, @RuleId INT', @FileLogId, @RuleId;
            END TRY
            BEGIN CATCH
                IF XACT_STATE() = -1 THROW;
                INSERT INTO dbo.DB_Errors (UserName, ErrorNumber, ErrorState, ErrorSeverity, ErrorLine,
                                           ErrorProcedure, ErrorMessage, ErrorDateTime)
                VALUES (SUSER_SNAME(), ERROR_NUMBER(), ERROR_STATE(), ERROR_SEVERITY(), ERROR_LINE(),
                        'sp_Validation_ExecuteRules',
                        'Rule ' + CAST(@RuleId AS VARCHAR(20)) + ' failed: ' + ERROR_MESSAGE(), GETDATE());
            END CATCH
        END

        FETCH NEXT FROM val_cursor INTO @RuleId, @TargetTable, @TargetColumn, @ValidationType, @Param1, @Param2;
    END
    CLOSE val_cursor; DEALLOCATE val_cursor;
END
GO

-- sp_Validation_AutoCorrect
CREATE OR ALTER PROCEDURE [dbo].[sp_Validation_AutoCorrect]
(
    @FileLogId INT
)
AS
BEGIN

SET NOCOUNT ON;

DECLARE
    @TargetTable SYSNAME,
    @TargetColumn SYSNAME,
    @ValidationType NVARCHAR(50),
    @Param1 NVARCHAR(500),
    @AutoCorrectReplacement NVARCHAR(100),
    @SQL NVARCHAR(MAX);

------------------------------------------------------------
-- STEP 1: GLOBAL NORMALIZATION
------------------------------------------------------------
-- 1a. FIX EIN FORMATTING (Add hyphen to 9-digit strings)
-- For Employers
UPDATE Staging_Employers
SET PrimaryEIN = LEFT(PrimaryEIN, 2) + '-' + RIGHT(PrimaryEIN, 7)
WHERE FileLogId = @FileLogId
  AND LEN(PrimaryEIN) = 9
  AND PrimaryEIN NOT LIKE '%-%';

-- For Employees (Primary EIN and Associated EIN)
UPDATE Staging_Employees
SET PrimaryEIN = LEFT(PrimaryEIN, 2) + '-' + RIGHT(PrimaryEIN, 7)
WHERE FileLogId = @FileLogId
  AND LEN(PrimaryEIN) = 9
  AND PrimaryEIN NOT LIKE '%-%';

UPDATE Staging_Employees
SET EINAssociatedWithEE = LEFT(EINAssociatedWithEE, 2) + '-' + RIGHT(EINAssociatedWithEE, 7)
WHERE FileLogId = @FileLogId
  AND LEN(EINAssociatedWithEE) = 9
  AND EINAssociatedWithEE NOT LIKE '%-%';

UPDATE Staging_Employers
SET Zip = '0' + CAST(Zip AS VARCHAR(5))
WHERE FileLogId = @FileLogId
  AND LEN(CAST(Zip AS VARCHAR(5))) = 4
  AND Zip NOT LIKE '%[^0-9]%';

UPDATE Staging_Employees
SET Zip = '0' + CAST(Zip AS VARCHAR(5))
WHERE FileLogId = @FileLogId
  AND LEN(CAST(Zip AS VARCHAR(5))) = 4
  AND Zip NOT LIKE '%[^0-9]%';

UPDATE Staging_Employers
SET Phone = REPLACE(REPLACE(REPLACE(REPLACE(Phone,'(',''),')',''),'-',''),' ','')
WHERE FileLogId=@FileLogId;

UPDATE Staging_Employers
SET
EmployerName = UPPER(ISNULL(EmployerName,'')),
Address = UPPER(ISNULL(Address,'')),
Address2 = UPPER(ISNULL(Address2,'')),
City = UPPER(ISNULL(City,'')),
Contact = UPPER(ISNULL(Contact,'')),
Title = UPPER(ISNULL(Title,''))
WHERE FileLogId=@FileLogId;

UPDATE Staging_Employees
SET
EmployeeLegalFirstName = UPPER(ISNULL(EmployeeLegalFirstName,'')),
EmployeeLegalLastName = UPPER(ISNULL(EmployeeLegalLastName,'')),
City = UPPER(ISNULL(City,''))
WHERE FileLogId=@FileLogId;

UPDATE Staging_Dependents
SET
DependentLegalFirstName = UPPER(ISNULL(DependentLegalFirstName,'')),
DependentLegalLastName = UPPER(ISNULL(DependentLegalLastName,'')),
DependentMiddleInitial = UPPER(ISNULL(DependentMiddleInitial,''))
WHERE FileLogId=@FileLogId;

------------------------------------------------------------
-- 055: THE VALUES THE STEPS BELOW MAY CHANGE, to record what changed. Taken
-- after step 1's formatting (upper case, EIN hyphen, ZIP leading zero, phone
-- digits), which is not reported.
------------------------------------------------------------
DELETE FROM dbo.StagingAutoCorrections WHERE FileLogId = @FileLogId;
IF OBJECT_ID('tempdb..#AcBefore') IS NOT NULL DROP TABLE #AcBefore;
CREATE TABLE #AcBefore (TableName SYSNAME NOT NULL, RowId BIGINT NOT NULL, ColumnName SYSNAME NOT NULL, RuleId INT NULL,
                        OldValue NVARCHAR(4000) NULL, PRIMARY KEY (TableName, ColumnName, RowId));
DECLARE @AcTable SYSNAME, @AcColumn SYSNAME, @AcRule INT, @AcCondition NVARCHAR(MAX), @AcPk SYSNAME;
DECLARE @AcCandidates TABLE (Seq INT IDENTITY PRIMARY KEY, TableName SYSNAME, ColumnName SYSNAME, RuleId INT NULL, Condition NVARCHAR(MAX));
-- The auto-correct rules, with the condition under which each changes a value.
INSERT @AcCandidates (TableName, ColumnName, RuleId, Condition)
SELECT TargetTable, TargetColumn, RuleId,
       CASE ValidationType
            WHEN 'REGEX_CONTAINS' THEN QUOTENAME(TargetColumn) + N' LIKE N''' + REPLACE(ISNULL(RuleParameter1, ''), '''', '''''') + N''''
            WHEN 'MAX_LENGTH'     THEN N'LEN(' + QUOTENAME(TargetColumn) + N') > ' + CAST(TRY_CAST(RuleParameter1 AS INT) AS NVARCHAR(10))
            ELSE N'ISNULL(' + QUOTENAME(TargetColumn) + N', '''') = '''''
       END
FROM   ValidationRules
WHERE  IsActive = 1 AND ISNULL(IsAutoCorrect, 0) = 1
  AND  ValidationType IN ('REGEX_CONTAINS', 'MAX_LENGTH', 'REQUIRED', 'REQUIRED_IF_EQUALS')
  AND  (ValidationType <> 'MAX_LENGTH' OR TRY_CAST(RuleParameter1 AS INT) IS NOT NULL)
  AND  EXISTS (SELECT 1 FROM StagingTableMetadata M WHERE M.TableName = TargetTable)
  AND  EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.' + QUOTENAME(TargetTable)) AND c.name = TargetColumn);
-- Step 2's name cleaning and steps 3-4's employer-name cleaning.
INSERT @AcCandidates (TableName, ColumnName, RuleId, Condition) VALUES
    ('Staging_Employees', 'EmployeeLegalFirstName', NULL, N'[EmployeeLegalFirstName] LIKE N''%[^A-Za-z ''''-]%'''),
    ('Staging_Employees', 'EmployeeLegalLastName',  NULL, N'[EmployeeLegalLastName] LIKE N''%[^A-Za-z ''''-]%'''),
    ('Staging_Employees', 'EmployeeMiddleInitial',  NULL, N'[EmployeeMiddleInitial] LIKE N''%[^A-Za-z ''''-]%'''),
    ('Staging_Employers', 'EmployerName',           NULL, N'[EmployerName] LIKE N''%[^A-Z0-9()&'''' -]%'' OR [EmployerName] LIKE N''%  %''');
DECLARE @AcSeq INT = 1, @AcMax INT = (SELECT MAX(Seq) FROM @AcCandidates);
WHILE @AcSeq <= @AcMax
BEGIN
    SELECT @AcTable = TableName, @AcColumn = ColumnName, @AcRule = RuleId, @AcCondition = Condition FROM @AcCandidates WHERE Seq = @AcSeq;
    SELECT @AcPk = PrimaryKeyColumn FROM StagingTableMetadata WHERE TableName = @AcTable;
    SET @SQL = N'INSERT INTO #AcBefore (TableName, RowId, ColumnName, RuleId, OldValue)
        SELECT @T, T.' + QUOTENAME(@AcPk) + N', @C, @R, CONVERT(NVARCHAR(4000), T.' + QUOTENAME(@AcColumn) + N')
        FROM ' + QUOTENAME(@AcTable) + N' T
        WHERE T.FileLogId = @FileLogId AND (' + @AcCondition + N')
          AND NOT EXISTS (SELECT 1 FROM #AcBefore B WHERE B.TableName = @T AND B.ColumnName = @C AND B.RowId = T.' + QUOTENAME(@AcPk) + N');';
    EXEC sp_executesql @SQL, N'@FileLogId INT, @T SYSNAME, @C SYSNAME, @R INT', @FileLogId, @AcTable, @AcColumn, @AcRule;
    SET @AcSeq += 1;
END

------------------------------------------------------------
-- STEP 2: STRICT NAME CLEANING (THE BUG FIX)
------------------------------------------------------------
-- This ensures we only keep Letters, Spaces, and Hyphens. 
-- The CASE statements ensure we don't return NULL when STUFF fails.
DECLARE @NamePattern NVARCHAR(100) = '%[^A-Za-z ''-]%'; 

WHILE EXISTS (SELECT 1 FROM Staging_Employees WHERE FileLogId = @FileLogId 
              AND (EmployeeLegalFirstName LIKE @NamePattern 
                   OR EmployeeLegalLastName LIKE @NamePattern 
                   OR EmployeeMiddleInitial LIKE @NamePattern))
BEGIN
    UPDATE TOP (5000) Staging_Employees
    SET EmployeeLegalFirstName = CASE 
            WHEN PATINDEX(@NamePattern, EmployeeLegalFirstName) > 0 
            THEN STUFF(EmployeeLegalFirstName, PATINDEX(@NamePattern, EmployeeLegalFirstName), 1, '') 
            ELSE EmployeeLegalFirstName END,
        
        EmployeeLegalLastName = CASE 
            WHEN PATINDEX(@NamePattern, EmployeeLegalLastName) > 0 
            THEN STUFF(EmployeeLegalLastName, PATINDEX(@NamePattern, EmployeeLegalLastName), 1, '') 
            ELSE EmployeeLegalLastName END,
            
        EmployeeMiddleInitial = CASE 
            WHEN PATINDEX(@NamePattern, EmployeeMiddleInitial) > 0 
            THEN STUFF(EmployeeMiddleInitial, PATINDEX(@NamePattern, EmployeeMiddleInitial), 1, '') 
            ELSE EmployeeMiddleInitial END
    WHERE FileLogId = @FileLogId 
      AND (EmployeeLegalFirstName LIKE @NamePattern OR EmployeeLegalLastName LIKE @NamePattern OR EmployeeMiddleInitial LIKE @NamePattern);
END

------------------------------------------------------------
-- STEP 3: AUTO CORRECT RULE LOOP
------------------------------------------------------------
DECLARE rule_cursor CURSOR FAST_FORWARD FOR
SELECT TargetTable, TargetColumn, ValidationType, ISNULL(RuleParameter1,''), ISNULL(AutoCorrectReplacement,'')
FROM ValidationRules WHERE IsActive=1 AND ISNULL(IsAutoCorrect,0)=1 ORDER BY RuleId;

OPEN rule_cursor;
FETCH NEXT FROM rule_cursor INTO @TargetTable, @TargetColumn, @ValidationType, @Param1, @AutoCorrectReplacement;

WHILE @@FETCH_STATUS=0
BEGIN

SET @Param1 = REPLACE(@Param1,'''','''''');
SET @AutoCorrectReplacement = REPLACE(@AutoCorrectReplacement,'''','''''');

IF @ValidationType='TRIM'
BEGIN
    SET @SQL='UPDATE T SET '+QUOTENAME(@TargetColumn)+' = UPPER(LTRIM(RTRIM('+QUOTENAME(@TargetColumn)+'))) FROM '+QUOTENAME(@TargetTable)+' T WHERE FileLogId=@FileLogId';
    EXEC sp_executesql @SQL, N'@FileLogId INT', @FileLogId;
END
ELSE IF @ValidationType='REQUIRED'
BEGIN
    SET @SQL='UPDATE T SET '+QUOTENAME(@TargetColumn)+' = UPPER('''+ISNULL(@AutoCorrectReplacement,'UNKNOWN')+''') FROM '+QUOTENAME(@TargetTable)+' T WHERE FileLogId=@FileLogId AND ISNULL('+QUOTENAME(@TargetColumn)+','''')=''''';
    EXEC sp_executesql @SQL, N'@FileLogId INT', @FileLogId;
END
ELSE IF @ValidationType='MAX_LENGTH' AND ISNUMERIC(@Param1)=1
BEGIN
    SET @SQL='UPDATE T SET '+QUOTENAME(@TargetColumn)+' = UPPER(LEFT('+QUOTENAME(@TargetColumn)+','+@Param1+')) FROM '+QUOTENAME(@TargetTable)+' T WHERE FileLogId=@FileLogId AND LEN('+QUOTENAME(@TargetColumn)+')>'+@Param1;
    EXEC sp_executesql @SQL, N'@FileLogId INT', @FileLogId;
END
ELSE IF @ValidationType='REGEX_CONTAINS'
BEGIN
    SET @SQL='
    DECLARE @Rows INT=1;
    WHILE @Rows>0
    BEGIN
        UPDATE TOP (5000) T
        SET '+QUOTENAME(@TargetColumn)+' = UPPER(STUFF('+QUOTENAME(@TargetColumn)+', PATINDEX('''+@Param1+''','+QUOTENAME(@TargetColumn)+'), 1, '''+ISNULL(@AutoCorrectReplacement,'')+'''))
        FROM '+QUOTENAME(@TargetTable)+' T
        WHERE FileLogId=@FileLogId AND '+QUOTENAME(@TargetColumn)+' LIKE '''+@Param1+'''
        SET @Rows=@@ROWCOUNT;
    END';
    EXEC sp_executesql @SQL, N'@FileLogId INT', @FileLogId;
END

IF @TargetColumn='ForeignAddressInd' AND @ValidationType='REQUIRED_IF_EQUALS'
BEGIN
    SET @SQL='
    UPDATE '+QUOTENAME(@TargetTable)+' SET ForeignAddressInd=''0'' WHERE FileLogId=@FileLogId AND (ForeignAddressInd IS NULL OR ForeignAddressInd='''') AND UPPER(Country)=''US'';
    UPDATE '+QUOTENAME(@TargetTable)+' SET ForeignAddressInd=''1'' WHERE FileLogId=@FileLogId AND (ForeignAddressInd IS NULL OR ForeignAddressInd='''') AND Country<>''US'' AND Country IS NOT NULL;';
    EXEC sp_executesql @SQL, N'@FileLogId INT', @FileLogId;
END

FETCH NEXT FROM rule_cursor INTO @TargetTable, @TargetColumn, @ValidationType, @Param1, @AutoCorrectReplacement;
END

CLOSE rule_cursor;
DEALLOCATE rule_cursor;

------------------------------------------------------------
-- STEP 3: STRICT CLEANING (ENTERPRISE SAFE)
------------------------------------------------------------
DECLARE @StrictEmp NVARCHAR(100)='%[^A-Z0-9()&'' -]%';

WHILE EXISTS (SELECT 1 FROM Staging_Employers WHERE FileLogId=@FileLogId AND EmployerName LIKE @StrictEmp)
BEGIN
    UPDATE TOP (5000) Staging_Employers
    SET EmployerName = UPPER(STUFF(EmployerName, PATINDEX(@StrictEmp,EmployerName), 1, ''))
    WHERE FileLogId=@FileLogId;
END

------------------------------------------------------------
-- STEP 4: FINAL SPACE CLEANUP
------------------------------------------------------------
UPDATE Staging_Employers
SET EmployerName = REPLACE(REPLACE(REPLACE(EmployerName, '   ',' '), '  ',' '), '  ',' ')
WHERE FileLogId=@FileLogId;


------------------------------------------------------------
-- 055: RECORD WHAT CHANGED, and show each change as an Information note on
-- its row (Information notes do not affect the import or the file's status).
------------------------------------------------------------
DECLARE ac_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT DISTINCT TableName, ColumnName FROM #AcBefore;
OPEN ac_cursor;
FETCH NEXT FROM ac_cursor INTO @AcTable, @AcColumn;
WHILE @@FETCH_STATUS = 0
BEGIN
    SELECT @AcPk = PrimaryKeyColumn FROM StagingTableMetadata WHERE TableName = @AcTable;
    SET @SQL = N'INSERT INTO dbo.StagingAutoCorrections (FileLogId, StagingTableName, StagingRowId, ColumnName, RuleId, OldValue, NewValue)
        SELECT @FileLogId, B.TableName, B.RowId, B.ColumnName, B.RuleId, B.OldValue, CONVERT(NVARCHAR(4000), T.' + QUOTENAME(@AcColumn) + N')
        FROM #AcBefore B JOIN ' + QUOTENAME(@AcTable) + N' T ON T.' + QUOTENAME(@AcPk) + N' = B.RowId
        WHERE B.TableName = @T AND B.ColumnName = @C
          AND UPPER(LTRIM(RTRIM(ISNULL(B.OldValue, N'''')))) COLLATE Latin1_General_BIN2
              <> UPPER(LTRIM(RTRIM(ISNULL(CONVERT(NVARCHAR(4000), T.' + QUOTENAME(@AcColumn) + N'), N'''')))) COLLATE Latin1_General_BIN2;';
    EXEC sp_executesql @SQL, N'@FileLogId INT, @T SYSNAME, @C SYSNAME', @FileLogId, @AcTable, @AcColumn;
    FETCH NEXT FROM ac_cursor INTO @AcTable, @AcColumn;
END
CLOSE ac_cursor;
DEALLOCATE ac_cursor;
DROP TABLE #AcBefore;

INSERT INTO dbo.StagingRowErrors (FileLogId, StagingTableName, StagingRowId, RuleId, Status, ColumnName, Detail)
SELECT C.FileLogId, C.StagingTableName, C.StagingRowId, R.RuleId, 'New', C.ColumnName,
       LEFT(N'Corrected automatically: "' + ISNULL(C.OldValue, N'') + N'" became "' + ISNULL(C.NewValue, N'') + N'".', 500)
FROM   dbo.StagingAutoCorrections C
CROSS JOIN (SELECT TOP 1 RuleId FROM dbo.ValidationRules WHERE RuleCode = 'AUTO_CORRECTED' ORDER BY RuleId) R
WHERE  C.FileLogId = @FileLogId;
END
GO

-- sp_ImportAnalysis_GetRows
CREATE OR ALTER PROCEDURE dbo.sp_ImportAnalysis_GetRows
    @FileLogId int, @Entity varchar(20)='Employer', @ImportMode varchar(20)='Merge',
    @Start int=0, @Length int=10, @Search nvarchar(200)=NULL,
    @SortColumn sysname='rowNumber', @SortDirection varchar(4)='asc',
    @ImpactFilter varchar(12)='All', @ValidationFilter varchar(12)='All'
AS
BEGIN
    SET NOCOUNT ON;
    IF @Start IS NULL OR @Start<0 OR @Length IS NULL OR @Length NOT BETWEEN 1 AND 100 THROW 51003,'Invalid page.',1;
    IF @ImportMode IS NULL OR @ImportMode NOT IN ('Merge','Overwrite') THROW 51001,'Invalid import mode.',1;
    IF @SortDirection IS NULL OR @SortDirection NOT IN ('asc','desc') THROW 51004,'Invalid sort direction.',1;
    IF @ImpactFilter IS NULL OR @ImpactFilter NOT IN ('All','INSERT','UPDATE','NOCHANGE','REMOVE','EXCLUDED') THROW 51005,'Invalid change filter.',1;
    IF @ValidationFilter IS NULL OR @ValidationFilter NOT IN ('All','Clean','Warning','Error','Information') THROW 51006,'Invalid validation filter.',1;
    IF NOT EXISTS (SELECT 1 FROM dbo.fn_ImportAnalysis_Context(@FileLogId) WHERE PlanYear BETWEEN 1900 AND 9999)
        THROW 51002,'File or filing year is unavailable.',1;

    DECLARE @Table sysname,@Pk sysname;
    SELECT @Table=T,@Pk=P FROM (VALUES

        ('Employer','Staging_Employers','StagingEmployerId'),
        ('Plan','Staging_Plans','StagingPlanId'),
        ('Premium','Staging_Premiums','StagingPremiumId'),
        ('Employee','Staging_Employees','StagingEmployeeId'),
        ('Dependent','Staging_Dependents','StagingDependentId')
    ) M(E,T,P) WHERE E=@Entity;
    IF @Table IS NULL THROW 51007,'Invalid sheet.',1;
    DECLARE @Columns TABLE (Ordinal int,ColumnName sysname);

    IF @Entity='Employer' INSERT @Columns VALUES
        (0,'PrimaryEIN'),
        (1,'AffiliatedEIN'),
        (2,'EmployerName'),
        (3,'ForeignAddressInd'),
        (4,'Address'),
        (5,'Address2'),
        (6,'City'),
        (7,'StateOrProvince'),
        (8,'Zip'),
        (9,'Country'),
        (10,'Phone'),
        (11,'Contact'),
        (12,'Title'),
        (13,'OriginCode'),
        (14,'SHOPIdentifier'),
        (15,'Notes');

    IF @Entity='Plan' INSERT @Columns VALUES
        (0,'PrimaryEIN'),
        (1,'PlanName'),
        (2,'PlanType'),
        (3,'OfferedToSpouse'),
        (4,'OfferedToDependents'),
        (5,'WaitingPeriodNumberOfDays'),
        (6,'EligibleFirstOfTheMonth'),
        (7,'FundingType'),
        (8,'PlanRenewalMonth'),
        (9,'PlanTerminatesOnDateOfTermination'),
        (10,'MinimumValue'),
        (11,'BandingType'),
        (12,'PremiumCap');

    IF @Entity='Premium' INSERT @Columns VALUES
        (0,'PrimaryEIN'),
        (1,'PlanName'),
        (2,'BandingType'),
        (3,'Start'),
        (4,'End'),
        (5,'StartDate'),
        (6,'EndDate'),
        (7,'EEMonthlyContribution'),
        (8,'Notes');

    IF @Entity='Employee' INSERT @Columns VALUES
        (0,'PrimaryEIN'),
        (1,'EINAssociatedWithEE'),
        (2,'EmployeeLegalFirstName'),
        (3,'EmployeeMiddleInitial'),
        (4,'EmployeeLegalLastName'),
        (5,'EmployeeSuffix'),
        (6,'EmployeeSSN'),
        (7,'EmployeeBirthdate'),
        (8,'Status'),
        (9,'Expatriate'),
        (10,'StatusStartDate'),
        (11,'StatusEndDate'),
        (12,'HireDate'),
        (13,'TerminationDate'),
        (14,'W2IncomeOrAnnualSalary'),
        (15,'AdditionalIncome'),
        (16,'HourlyRate'),
        (17,'EmailAddress'),
        (18,'ForeignAddress'),
        (19,'Address1'),
        (20,'Address2'),
        (21,'City'),
        (22,'StateOrProvince'),
        (23,'Zip'),
        (24,'Country'),
        (25,'LowestCostPlanOffered'),
        (26,'DateEligibleForCoverage'),
        (27,'CoverageElected'),
        (28,'CoverageStartDate'),
        (29,'CoverageEndDate'),
        (30,'UnionEmployee'),
        (31,'DateEmployerFirstPaysToUnionBenefits'),
        (32,'DateEmployerLastPaysToUnionBenefits'),
        (33,'COBRABenefitsElected'),
        (34,'COBRACoverageEffectiveDate'),
        (35,'COBRACoverageEndDate'),
        (36,'RetireeBenefitsElected'),
        (37,'RetireePlanStartDate'),
        (38,'RetireePlanEndDate');

    IF @Entity='Dependent' INSERT @Columns VALUES
        (0,'PrimaryEIN'),
        (1,'EINAssociatedWithEE'),
        (2,'EmployeeSSN'),
        (3,'DependentLegalFirstName'),
        (4,'DependentMiddleInitial'),
        (5,'DependentLegalLastName'),
        (6,'DependentSuffix'),
        (7,'DependentSSN'),
        (8,'DependentBirthdate'),
        (9,'DependentCoverageStartDate'),
        (10,'DependentCoverageEndDate'),
        (11,'Relationship');

    IF @SortColumn IS NULL OR (@SortColumn NOT IN ('rowNumber','actionType','validationStatus')
        AND NOT EXISTS (SELECT 1 FROM @Columns WHERE ColumnName=@SortColumn)) THROW 51008,'Invalid sort column.',1;

    SELECT * INTO #Analysis FROM dbo.fn_ImportAnalysis_Rows(@FileLogId,@Entity,@ImportMode) OPTION (RECOMPILE);
    CREATE UNIQUE CLUSTERED INDEX IX_Analysis_Source_Row ON #Analysis(RowSource,RowId);
    CREATE INDEX IX_Analysis_DefaultPage ON #Analysis(RowNumber,RowSource,RowId);

    DECLARE @Projection nvarchar(max),@SearchPredicate nvarchar(max),@LiveProjection nvarchar(max)=N'',@LiveFrom nvarchar(max)=N'';
    SELECT @Projection=STUFF((SELECT N',CONVERT(nvarchar(max),S.'+QUOTENAME(ColumnName)+N') AS '+QUOTENAME(ColumnName)
        FROM @Columns ORDER BY Ordinal FOR XML PATH(''),TYPE).value('.','nvarchar(max)'),1,1,N'');
    SELECT @SearchPredicate=STUFF((SELECT N' OR R.'+QUOTENAME(ColumnName)+N' LIKE @Pattern ESCAPE N'+CHAR(39)+N'~'+CHAR(39)
        FROM @Columns ORDER BY Ordinal FOR XML PATH(''),TYPE).value('.','nvarchar(max)'),1,4,N'');

    IF @Entity='Premium'
    BEGIN
        SET @LiveProjection=N'CONVERT(nvarchar(max),C.PrimaryEIN) AS [PrimaryEIN],CONVERT(nvarchar(max),P.name) AS [PlanName],CONVERT(nvarchar(max),P.bandingType) AS [BandingType],CONVERT(nvarchar(max),S.bandingValueStart) AS [Start],CONVERT(nvarchar(max),S.bandingValueEnd) AS [End],CONVERT(nvarchar(max),CONVERT(varchar(10),S.bandingStartDate,23)) AS [StartDate],CONVERT(nvarchar(max),CONVERT(varchar(10),S.bandingEndDate,23)) AS [EndDate],CONVERT(nvarchar(max),S.amount) AS [EEMonthlyContribution],CONVERT(nvarchar(max),NULL) AS [Notes]';
        SET @LiveFrom=N'dbo.Premium S JOIN dbo.EmployerPlan P ON P.id=S.EmployerPlanId CROSS APPLY dbo.fn_ImportAnalysis_Context(@FileLogId) C';
    END;

    IF @Entity='Dependent'
    BEGIN
        SET @LiveProjection=N'CONVERT(nvarchar(max),C.PrimaryEIN) AS [PrimaryEIN],CONVERT(nvarchar(max),EM.taxid) AS [EINAssociatedWithEE],CONVERT(nvarchar(max),E.ssn) AS [EmployeeSSN],CONVERT(nvarchar(max),S.firstName) AS [DependentLegalFirstName],CONVERT(nvarchar(max),S.middleName) AS [DependentMiddleInitial],CONVERT(nvarchar(max),S.lastName) AS [DependentLegalLastName],CONVERT(nvarchar(max),S.suffix) AS [DependentSuffix],CONVERT(nvarchar(max),S.ssn) AS [DependentSSN],CONVERT(nvarchar(max),CONVERT(varchar(10),S.birthday,23)) AS [DependentBirthdate],CONVERT(nvarchar(max),CONVERT(varchar(10),S.coverageStartDate,23)) AS [DependentCoverageStartDate],CONVERT(nvarchar(max),CONVERT(varchar(10),S.coverageEndDate,23)) AS [DependentCoverageEndDate],CONVERT(nvarchar(max),S.Relationship) AS [Relationship]';
        SET @LiveFrom=N'dbo.CoveredIndividual S JOIN dbo.EmployeeCode EC ON EC.id=S.employeeCodeId JOIN dbo.Employee E ON E.id=EC.employeeId JOIN dbo.Employer EM ON EM.id=EC.employerId CROSS APPLY dbo.fn_ImportAnalysis_Context(@FileLogId) C';
    END;

    DECLARE @Base nvarchar(max)=N'SELECT A.RowId AS rowId,A.RowNumber AS rowNumber,A.RowSource AS rowSource,
        A.ActionType AS actionType,A.ValidationStatus AS validationStatus,A.ActionNote AS actionNote,
        S.ErrorMessages AS errorMessages,'+@Projection+N'
        FROM #Analysis A JOIN dbo.'+QUOTENAME(@Table)+N' S ON S.'+QUOTENAME(@Pk)+N'=A.RowId
        AND S.FileLogId=@FileLogId WHERE A.RowSource=''Upload''';
    IF @LiveFrom<>N'' SET @Base+=N' UNION ALL
        SELECT A.RowId,A.RowNumber,A.RowSource,A.ActionType,A.ValidationStatus,A.ActionNote,CONVERT(nvarchar(max),NULL),'+@LiveProjection+N'
        FROM '+@LiveFrom+N' JOIN #Analysis A ON A.RowSource=''Database'' AND S.id=A.RowId';

    -- Bind all values. Only catalog-approved identifiers/direction are concatenated.
    DECLARE @Pattern nvarchar(410)=NULL;
    IF NULLIF(LTRIM(RTRIM(@Search)),N'') IS NOT NULL
        SET @Pattern=N'%'+REPLACE(REPLACE(REPLACE(REPLACE(@Search,N'~',N'~~'),N'%',N'~%'),N'_',N'~_'),N'[',N'~[')+N'%';
    DECLARE @Where nvarchar(max)=N' WHERE (@ImpactFilter=''All'' OR R.actionType=@ImpactFilter)
        AND (@ValidationFilter=''All'' OR R.validationStatus=@ValidationFilter)
        AND (@Pattern IS NULL OR '+@SearchPredicate+N')';
    DECLARE @SortExpression nvarchar(300)=N'R.'+QUOTENAME(@SortColumn);
    IF @SortColumn IN ('EmployeeBirthdate','DependentBirthdate','StartDate','EndDate','StatusStartDate','StatusEndDate',
        'HireDate','TerminationDate','DateEligibleForCoverage','CoverageStartDate','CoverageEndDate',
        'DateEmployerFirstPaysToUnionBenefits','DateEmployerLastPaysToUnionBenefits','COBRACoverageEffectiveDate',
        'COBRACoverageEndDate','RetireePlanStartDate','RetireePlanEndDate','DependentCoverageStartDate','DependentCoverageEndDate')
        SET @SortExpression=N'TRY_CONVERT(date,'+@SortExpression+N')';
    ELSE IF @SortColumn IN ('PremiumCap','EEMonthlyContribution','W2IncomeOrAnnualSalary','AdditionalIncome','HourlyRate',
        'WaitingPeriodNumberOfDays','PlanRenewalMonth','Status')
        SET @SortExpression=N'TRY_CONVERT(decimal(38,10),'+@SortExpression+N')';
    DECLARE @Order nvarchar(500)=@SortExpression+N' '+@SortDirection+N',R.rowSource,R.rowId';
    DECLARE @Sql nvarchar(max)=N'
        SELECT (SELECT COUNT(*) FROM #Analysis) AS RecordsTotal,COUNT(*) AS RecordsFiltered FROM ('+@Base+N') R'+@Where+N';
        SELECT P.*,
            (SELECT ISNULL(E.ColumnName,V.TargetColumn) AS [column],V.Severity AS severity,ISNULL(E.Detail,V.ErrorMessage) AS message
             FROM dbo.StagingRowErrors E JOIN dbo.ValidationRules V ON V.RuleId=E.RuleId
             WHERE P.rowSource=''Upload'' AND E.FileLogId=@FileLogId AND E.StagingTableName=@TableName AND E.StagingRowId=P.rowId
               AND E.Status NOT IN (''Resolved'',''Fixed'',''Ignored'',''Dismissed'')
             ORDER BY E.StagingRowErrorId FOR JSON PATH) AS issuesJson
        FROM (SELECT R.* FROM ('+@Base+N') R'+@Where+N' ORDER BY '+@Order+N'
              OFFSET @Start ROWS FETCH NEXT @Length ROWS ONLY) P
        ORDER BY '+REPLACE(@SortExpression,N'R.',N'P.')+N' '+@SortDirection+N',P.rowSource,P.rowId;';
    EXEC sys.sp_executesql @Sql,
        N'@FileLogId int,@TableName sysname,@Start int,@Length int,@Pattern nvarchar(410),@ImpactFilter varchar(12),@ValidationFilter varchar(12)',
        @FileLogId,@Table,@Start,@Length,@Pattern,@ImpactFilter,@ValidationFilter;
END;
GO

-- sp_ImportAnalysis_ExportRows
CREATE OR ALTER PROCEDURE dbo.sp_ImportAnalysis_ExportRows
    @FileLogId int, @Entity varchar(20), @ImportMode varchar(20)='Merge'
AS
BEGIN
    SET NOCOUNT ON;
    IF @ImportMode IS NULL OR @ImportMode NOT IN ('Merge','Overwrite') THROW 51001,'Invalid import mode.',1;
    IF NOT EXISTS (SELECT 1 FROM dbo.fn_ImportAnalysis_Context(@FileLogId) WHERE PlanYear BETWEEN 1900 AND 9999)
        THROW 51002,'File or filing year is unavailable.',1;
    DECLARE @Table sysname,@Pk sysname;
    SELECT @Table=T,@Pk=P FROM (VALUES

        ('Employer','Staging_Employers','StagingEmployerId'),
        ('Plan','Staging_Plans','StagingPlanId'),
        ('Premium','Staging_Premiums','StagingPremiumId'),
        ('Employee','Staging_Employees','StagingEmployeeId'),
        ('Dependent','Staging_Dependents','StagingDependentId')
    ) M(E,T,P) WHERE E=@Entity;
    IF @Table IS NULL THROW 51007,'Invalid sheet.',1;
    DECLARE @Columns TABLE (Ordinal int,ColumnName sysname);

    IF @Entity='Employer' INSERT @Columns VALUES
        (0,'PrimaryEIN'),
        (1,'AffiliatedEIN'),
        (2,'EmployerName'),
        (3,'ForeignAddressInd'),
        (4,'Address'),
        (5,'Address2'),
        (6,'City'),
        (7,'StateOrProvince'),
        (8,'Zip'),
        (9,'Country'),
        (10,'Phone'),
        (11,'Contact'),
        (12,'Title'),
        (13,'OriginCode'),
        (14,'SHOPIdentifier'),
        (15,'Notes');

    IF @Entity='Plan' INSERT @Columns VALUES
        (0,'PrimaryEIN'),
        (1,'PlanName'),
        (2,'PlanType'),
        (3,'OfferedToSpouse'),
        (4,'OfferedToDependents'),
        (5,'WaitingPeriodNumberOfDays'),
        (6,'EligibleFirstOfTheMonth'),
        (7,'FundingType'),
        (8,'PlanRenewalMonth'),
        (9,'PlanTerminatesOnDateOfTermination'),
        (10,'MinimumValue'),
        (11,'BandingType'),
        (12,'PremiumCap');

    IF @Entity='Premium' INSERT @Columns VALUES
        (0,'PrimaryEIN'),
        (1,'PlanName'),
        (2,'BandingType'),
        (3,'Start'),
        (4,'End'),
        (5,'StartDate'),
        (6,'EndDate'),
        (7,'EEMonthlyContribution'),
        (8,'Notes');

    IF @Entity='Employee' INSERT @Columns VALUES
        (0,'PrimaryEIN'),
        (1,'EINAssociatedWithEE'),
        (2,'EmployeeLegalFirstName'),
        (3,'EmployeeMiddleInitial'),
        (4,'EmployeeLegalLastName'),
        (5,'EmployeeSuffix'),
        (6,'EmployeeSSN'),
        (7,'EmployeeBirthdate'),
        (8,'Status'),
        (9,'Expatriate'),
        (10,'StatusStartDate'),
        (11,'StatusEndDate'),
        (12,'HireDate'),
        (13,'TerminationDate'),
        (14,'W2IncomeOrAnnualSalary'),
        (15,'AdditionalIncome'),
        (16,'HourlyRate'),
        (17,'EmailAddress'),
        (18,'ForeignAddress'),
        (19,'Address1'),
        (20,'Address2'),
        (21,'City'),
        (22,'StateOrProvince'),
        (23,'Zip'),
        (24,'Country'),
        (25,'LowestCostPlanOffered'),
        (26,'DateEligibleForCoverage'),
        (27,'CoverageElected'),
        (28,'CoverageStartDate'),
        (29,'CoverageEndDate'),
        (30,'UnionEmployee'),
        (31,'DateEmployerFirstPaysToUnionBenefits'),
        (32,'DateEmployerLastPaysToUnionBenefits'),
        (33,'COBRABenefitsElected'),
        (34,'COBRACoverageEffectiveDate'),
        (35,'COBRACoverageEndDate'),
        (36,'RetireeBenefitsElected'),
        (37,'RetireePlanStartDate'),
        (38,'RetireePlanEndDate');

    IF @Entity='Dependent' INSERT @Columns VALUES
        (0,'PrimaryEIN'),
        (1,'EINAssociatedWithEE'),
        (2,'EmployeeSSN'),
        (3,'DependentLegalFirstName'),
        (4,'DependentMiddleInitial'),
        (5,'DependentLegalLastName'),
        (6,'DependentSuffix'),
        (7,'DependentSSN'),
        (8,'DependentBirthdate'),
        (9,'DependentCoverageStartDate'),
        (10,'DependentCoverageEndDate'),
        (11,'Relationship');

    SELECT * INTO #Analysis FROM dbo.fn_ImportAnalysis_Rows(@FileLogId,@Entity,@ImportMode) OPTION (RECOMPILE);
    CREATE UNIQUE CLUSTERED INDEX IX_Analysis_Source_Row ON #Analysis(RowSource,RowId);
    CREATE INDEX IX_Analysis_DefaultPage ON #Analysis(RowNumber,RowSource,RowId);

    DECLARE @Projection nvarchar(max),@LiveProjection nvarchar(max)=N'',@LiveFrom nvarchar(max)=N'';
    SELECT @Projection=STUFF((SELECT N',CONVERT(nvarchar(max),S.'+QUOTENAME(ColumnName)+N') AS '+QUOTENAME(ColumnName)
        FROM @Columns ORDER BY Ordinal FOR XML PATH(''),TYPE).value('.','nvarchar(max)'),1,1,N'');
    IF @Entity='Premium'
    BEGIN
        SET @LiveProjection=N'CONVERT(nvarchar(max),C.PrimaryEIN) AS [PrimaryEIN],CONVERT(nvarchar(max),P.name) AS [PlanName],CONVERT(nvarchar(max),P.bandingType) AS [BandingType],CONVERT(nvarchar(max),S.bandingValueStart) AS [Start],CONVERT(nvarchar(max),S.bandingValueEnd) AS [End],CONVERT(nvarchar(max),CONVERT(varchar(10),S.bandingStartDate,23)) AS [StartDate],CONVERT(nvarchar(max),CONVERT(varchar(10),S.bandingEndDate,23)) AS [EndDate],CONVERT(nvarchar(max),S.amount) AS [EEMonthlyContribution],CONVERT(nvarchar(max),NULL) AS [Notes]';
        SET @LiveFrom=N'dbo.Premium S JOIN dbo.EmployerPlan P ON P.id=S.EmployerPlanId CROSS APPLY dbo.fn_ImportAnalysis_Context(@FileLogId) C';
    END;

    IF @Entity='Dependent'
    BEGIN
        SET @LiveProjection=N'CONVERT(nvarchar(max),C.PrimaryEIN) AS [PrimaryEIN],CONVERT(nvarchar(max),EM.taxid) AS [EINAssociatedWithEE],CONVERT(nvarchar(max),E.ssn) AS [EmployeeSSN],CONVERT(nvarchar(max),S.firstName) AS [DependentLegalFirstName],CONVERT(nvarchar(max),S.middleName) AS [DependentMiddleInitial],CONVERT(nvarchar(max),S.lastName) AS [DependentLegalLastName],CONVERT(nvarchar(max),S.suffix) AS [DependentSuffix],CONVERT(nvarchar(max),S.ssn) AS [DependentSSN],CONVERT(nvarchar(max),CONVERT(varchar(10),S.birthday,23)) AS [DependentBirthdate],CONVERT(nvarchar(max),CONVERT(varchar(10),S.coverageStartDate,23)) AS [DependentCoverageStartDate],CONVERT(nvarchar(max),CONVERT(varchar(10),S.coverageEndDate,23)) AS [DependentCoverageEndDate],CONVERT(nvarchar(max),S.Relationship) AS [Relationship]';
        SET @LiveFrom=N'dbo.CoveredIndividual S JOIN dbo.EmployeeCode EC ON EC.id=S.employeeCodeId JOIN dbo.Employee E ON E.id=EC.employeeId JOIN dbo.Employer EM ON EM.id=EC.employerId CROSS APPLY dbo.fn_ImportAnalysis_Context(@FileLogId) C';
    END;

    DECLARE @Base nvarchar(max)=N'SELECT A.RowId AS rowId,A.RowNumber AS rowNumber,A.RowSource AS rowSource,
        A.ActionType AS actionType,A.ValidationStatus AS validationStatus,A.ActionNote AS actionNote,
        S.ErrorMessages AS errorMessages,'+@Projection+N'
        FROM #Analysis A JOIN dbo.'+QUOTENAME(@Table)+N' S ON S.'+QUOTENAME(@Pk)+N'=A.RowId
        AND S.FileLogId=@FileLogId WHERE A.RowSource=''Upload''';
    IF @LiveFrom<>N'' SET @Base+=N' UNION ALL
        SELECT A.RowId,A.RowNumber,A.RowSource,A.ActionType,A.ValidationStatus,A.ActionNote,CONVERT(nvarchar(max),NULL),'+@LiveProjection+N'
        FROM '+@LiveFrom+N' JOIN #Analysis A ON A.RowSource=''Database'' AND S.id=A.RowId';

    DECLARE @Sql nvarchar(max)=N'
        SELECT P.*,
            (SELECT ISNULL(E.ColumnName,V.TargetColumn) AS [column],V.Severity AS severity,ISNULL(E.Detail,V.ErrorMessage) AS message
             FROM dbo.StagingRowErrors E JOIN dbo.ValidationRules V ON V.RuleId=E.RuleId
             WHERE P.rowSource=''Upload'' AND E.FileLogId=@FileLogId AND E.StagingTableName=@TableName AND E.StagingRowId=P.rowId
               AND E.Status NOT IN (''Resolved'',''Fixed'',''Ignored'',''Dismissed'')
             ORDER BY E.StagingRowErrorId FOR JSON PATH) AS issuesJson
        FROM ('+@Base+N') P
        ORDER BY P.rowNumber,P.rowSource,P.rowId;';
    EXEC sys.sp_executesql @Sql,N'@FileLogId int,@TableName sysname',@FileLogId,@Table;
END;
GO

-- sp_GetStagingErrorsByRowId
CREATE OR ALTER PROCEDURE [dbo].[sp_GetStagingErrorsByRowId]
    @FileLogId INT,
    @StagingTableName NVARCHAR(128),
    @StagingRowId BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        e.StagingRowErrorId, e.StagingTableName, e.StagingRowId, ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn,
        ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage, r.Severity, r.IsCorrectable, r.HelpText, e.Status
    FROM StagingRowErrors e
    JOIN ValidationRules r ON e.RuleId = r.RuleId
    WHERE 
        e.FileLogId = @FileLogId
        AND e.StagingTableName = @StagingTableName
        AND e.StagingRowId = @StagingRowId;
END
GO

-- sp_GetStagingErrorsForTriage
CREATE OR ALTER PROCEDURE [dbo].[sp_GetStagingErrorsForTriage]   --92  
    @FileLogId INT    
AS    
BEGIN    
    SET NOCOUNT ON;    
    
    -- This query gets all errors and joins to the appropriate staging table    
    -- to retrieve the original Excel RowNumber for each error.    
    
    -- Get errors from the Staging_Employers table    
    SELECT     
        e.StagingRowErrorId,    
        e.StagingTableName,    
        e.StagingRowId,    
        st.RowNumber, -- Get the original Excel row number    
        ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn,    
        ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage,    
        r.Severity,    
        r.IsCorrectable,    
        r.HelpText,    
        e.Status,  
  r.AuditTabNote  
  
    FROM StagingRowErrors e    
    JOIN ValidationRules r ON e.RuleId = r.RuleId    
    JOIN Staging_Employers st ON e.StagingRowId = st.StagingEmployerId    
    WHERE e.FileLogId = @FileLogId AND e.StagingTableName = 'Staging_Employers'    
    
    UNION ALL    
    
    -- Get errors from the Staging_Plans table    
    SELECT e.StagingRowErrorId, e.StagingTableName, e.StagingRowId, st.RowNumber, ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn, ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage, r.Severity, r.IsCorrectable, r.HelpText, e.Status,r.AuditTabNote    
    FROM StagingRowErrors e JOIN ValidationRules r ON e.RuleId = r.RuleId JOIN Staging_Plans st ON e.StagingRowId = st.StagingPlanId    
    WHERE e.FileLogId = @FileLogId AND e.StagingTableName = 'Staging_Plans'    
    
    UNION ALL    
    
    -- Get errors from the Staging_Premiums table    
    SELECT e.StagingRowErrorId, e.StagingTableName, e.StagingRowId, st.RowNumber, ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn, ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage, r.Severity, r.IsCorrectable, r.HelpText, e.Status,r.AuditTabNote      
    FROM StagingRowErrors e JOIN ValidationRules r ON e.RuleId = r.RuleId JOIN Staging_Premiums st ON e.StagingRowId = st.StagingPremiumId    
    WHERE e.FileLogId = @FileLogId AND e.StagingTableName = 'Staging_Premiums'    
    
    UNION ALL    
    
    -- Get errors from the Staging_Employees table    
    SELECT e.StagingRowErrorId, e.StagingTableName, e.StagingRowId, st.RowNumber, ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn, ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage, r.Severity, r.IsCorrectable, r.HelpText, e.Status,r.AuditTabNote      
    FROM StagingRowErrors e JOIN ValidationRules r ON e.RuleId = r.RuleId JOIN Staging_Employees st ON e.StagingRowId = st.StagingEmployeeId    
    WHERE e.FileLogId = @FileLogId AND e.StagingTableName = 'Staging_Employees'    
    
    UNION ALL    
    
    -- Get errors from the Staging_Dependents table    
    SELECT e.StagingRowErrorId, e.StagingTableName, e.StagingRowId, st.RowNumber, ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn, ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage, r.Severity, r.IsCorrectable, r.HelpText, e.Status,r.AuditTabNote      
    FROM StagingRowErrors e JOIN ValidationRules r ON e.RuleId = r.RuleId JOIN Staging_Dependents st ON e.StagingRowId = st.StagingDependentId    
    WHERE e.FileLogId = @FileLogId AND e.StagingTableName = 'Staging_Dependents';    
    
END
GO

-- sp_GetStagingErrorsForTriage_with_filter
CREATE OR ALTER PROCEDURE [dbo].[sp_GetStagingErrorsForTriage_with_filter]          
(         
    @FileLogId INT,         
    @PageIndex INT = NULL,         
    @PageSize INT = NULL,         
    @Search NVARCHAR(100) = NULL,         
    @SortColumn INT = null,  -- 0=RowNumber, 1=ErrorMessage, 2=Severity         
    @SortOrder VARCHAR(5) = 'asc',         
    @Severity NVARCHAR(50) = NULL, 
    @SearchColumn NVARCHAR(100) = NULL -- [NEW PARAMETER IN USE]
)         
AS         
BEGIN         
    DECLARE @RecordCount INT;         
    DECLARE @StartPage INT;         
    DECLARE @EndPage INT;         
    DECLARE @ErrorCount INT = 0;  
    DECLARE @WarningCount INT = 0;  
    DECLARE @InformationCount INT = 0;  
    DECLARE @EmployerActionCount INT = 0;  
    DECLARE @TotalErrorsCount INT = 0;    

    BEGIN TRY         
        BEGIN TRANSACTION         
        SET NOCOUNT ON;         
        
        CREATE TABLE #AllErrors         
        (         
            StagingRowErrorId INT,         
            StagingTableName NVARCHAR(100),         
            StagingRowId INT,         
            RowNumber INT,         
            TargetColumn NVARCHAR(100),         
            ErrorMessage NVARCHAR(MAX),         
            Severity NVARCHAR(50),         
            IsCorrectable BIT,         
            HelpText NVARCHAR(MAX),         
            Status NVARCHAR(50)         
        );         
        
        -- ==========================================================================
        -- FILTER LOGIC HELPER
        -- We apply this same logic to every INSERT statement below
        -- ==========================================================================
        -- Logic: 
        -- 1. If @Search is empty, match everything.
        -- 2. If @SearchColumn is empty, search ALL fields (Message, Column, Table).
        -- 3. If @SearchColumn is set, search ONLY that field.
        
        -- Special handling for Employer Action filter  
        IF @Severity = 'Employer_Action'  
        BEGIN  
            INSERT INTO #AllErrors         
            SELECT          
                e.StagingRowErrorId, e.StagingTableName, e.StagingRowId, st.RowNumber,         
                ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn, ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage, r.Severity, r.IsCorrectable, r.HelpText, e.Status         
            FROM StagingRowErrors e         
            JOIN ValidationRules r ON e.RuleId = r.RuleId         
            JOIN Staging_Employers st ON e.StagingRowId = st.StagingEmployerId         
            WHERE e.FileLogId = @FileLogId          
                AND e.StagingTableName = 'Staging_Employers'         
                AND r.IsCorrectable = 0  
                -- [SEARCH FILTER]
                AND (
                    ISNULL(@Search, '') = '' 
                    OR (
                        (@SearchColumn IS NULL OR @SearchColumn = '') AND (ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%' OR r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'ErrorMessage' AND ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'TargetColumn' AND r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'StagingTableName' AND e.StagingTableName LIKE '%' + @Search + '%')
                    )
                )

            UNION ALL         
        
            SELECT          
                e.StagingRowErrorId, e.StagingTableName, e.StagingRowId, st.RowNumber,         
                ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn, ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage, r.Severity, r.IsCorrectable, r.HelpText, e.Status         
            FROM StagingRowErrors e         
            JOIN ValidationRules r ON e.RuleId = r.RuleId         
            JOIN Staging_Plans st ON e.StagingRowId = st.StagingPlanId         
            WHERE e.FileLogId = @FileLogId          
                AND e.StagingTableName = 'Staging_Plans'         
                AND r.IsCorrectable = 0  
                -- [SEARCH FILTER]
                AND (
                    ISNULL(@Search, '') = '' 
                    OR (
                        (@SearchColumn IS NULL OR @SearchColumn = '') AND (ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%' OR r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'ErrorMessage' AND ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'TargetColumn' AND r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'StagingTableName' AND e.StagingTableName LIKE '%' + @Search + '%')
                    )
                )

            UNION ALL         
        
            SELECT          
                e.StagingRowErrorId, e.StagingTableName, e.StagingRowId, st.RowNumber,         
                ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn, ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage, r.Severity, r.IsCorrectable, r.HelpText, e.Status         
            FROM StagingRowErrors e         
            JOIN ValidationRules r ON e.RuleId = r.RuleId         
            JOIN Staging_Premiums st ON e.StagingRowId = st.StagingPremiumId         
            WHERE e.FileLogId = @FileLogId          
                AND e.StagingTableName = 'Staging_Premiums'         
                AND r.IsCorrectable = 0  
                -- [SEARCH FILTER]
                AND (
                    ISNULL(@Search, '') = '' 
                    OR (
                        (@SearchColumn IS NULL OR @SearchColumn = '') AND (ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%' OR r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'ErrorMessage' AND ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'TargetColumn' AND r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'StagingTableName' AND e.StagingTableName LIKE '%' + @Search + '%')
                    )
                )

            UNION ALL         
        
            SELECT          
                e.StagingRowErrorId, e.StagingTableName, e.StagingRowId, st.RowNumber,         
                ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn, ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage, r.Severity, r.IsCorrectable, r.HelpText, e.Status         
            FROM StagingRowErrors e         
            JOIN ValidationRules r ON e.RuleId = r.RuleId         
            JOIN Staging_Employees st ON e.StagingRowId = st.StagingEmployeeId         
            WHERE e.FileLogId = @FileLogId          
                AND e.StagingTableName = 'Staging_Employees'         
                AND r.IsCorrectable = 0  
                -- [SEARCH FILTER]
                AND (
                    ISNULL(@Search, '') = '' 
                    OR (
                        (@SearchColumn IS NULL OR @SearchColumn = '') AND (ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%' OR r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'ErrorMessage' AND ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'TargetColumn' AND r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'StagingTableName' AND e.StagingTableName LIKE '%' + @Search + '%')
                    )
                )

            UNION ALL         
        
            SELECT          
                e.StagingRowErrorId, e.StagingTableName, e.StagingRowId, st.RowNumber,         
                ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn, ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage, r.Severity, r.IsCorrectable, r.HelpText, e.Status         
            FROM StagingRowErrors e         
            JOIN ValidationRules r ON e.RuleId = r.RuleId         
            JOIN Staging_Dependents st ON e.StagingRowId = st.StagingDependentId         
            WHERE e.FileLogId = @FileLogId          
                AND e.StagingTableName = 'Staging_Dependents'         
                AND r.IsCorrectable = 0
                -- [SEARCH FILTER]
                AND (
                    ISNULL(@Search, '') = '' 
                    OR (
                        (@SearchColumn IS NULL OR @SearchColumn = '') AND (ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%' OR r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'ErrorMessage' AND ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'TargetColumn' AND r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'StagingTableName' AND e.StagingTableName LIKE '%' + @Search + '%')
                    )
                );  
        END  
        ELSE  
        BEGIN  
            -- STANDARD FILTER LOGIC (Not Employer Action)
            
            INSERT INTO #AllErrors         
            SELECT          
                e.StagingRowErrorId, e.StagingTableName, e.StagingRowId, st.RowNumber,         
                ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn, ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage, r.Severity, r.IsCorrectable, r.HelpText, e.Status         
            FROM StagingRowErrors e         
            JOIN ValidationRules r ON e.RuleId = r.RuleId         
            JOIN Staging_Employers st ON e.StagingRowId = st.StagingEmployerId         
            WHERE e.FileLogId = @FileLogId          
                AND e.StagingTableName = 'Staging_Employers'         
                AND (ISNULL(@Severity, '') = '' OR r.Severity = @Severity)         
                -- [SEARCH FILTER]
                AND (
                    ISNULL(@Search, '') = '' 
                    OR (
                        (@SearchColumn IS NULL OR @SearchColumn = '') AND (ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%' OR r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'ErrorMessage' AND ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'TargetColumn' AND r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'StagingTableName' AND e.StagingTableName LIKE '%' + @Search + '%')
                    )
                )
        
            UNION ALL         
        
            SELECT          
                e.StagingRowErrorId, e.StagingTableName, e.StagingRowId, st.RowNumber,         
                ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn, ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage, r.Severity, r.IsCorrectable, r.HelpText, e.Status         
            FROM StagingRowErrors e         
            JOIN ValidationRules r ON e.RuleId = r.RuleId         
            JOIN Staging_Plans st ON e.StagingRowId = st.StagingPlanId         
            WHERE e.FileLogId = @FileLogId          
                AND e.StagingTableName = 'Staging_Plans'         
                AND (ISNULL(@Severity, '') = '' OR r.Severity = @Severity)         
                -- [SEARCH FILTER]
                AND (
                    ISNULL(@Search, '') = '' 
                    OR (
                        (@SearchColumn IS NULL OR @SearchColumn = '') AND (ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%' OR r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'ErrorMessage' AND ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'TargetColumn' AND r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'StagingTableName' AND e.StagingTableName LIKE '%' + @Search + '%')
                    )
                )
        
            UNION ALL         
        
            SELECT          
                e.StagingRowErrorId, e.StagingTableName, e.StagingRowId, st.RowNumber,         
                ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn, ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage, r.Severity, r.IsCorrectable, r.HelpText, e.Status         
            FROM StagingRowErrors e         
            JOIN ValidationRules r ON e.RuleId = r.RuleId         
            JOIN Staging_Premiums st ON e.StagingRowId = st.StagingPremiumId         
            WHERE e.FileLogId = @FileLogId          
                AND e.StagingTableName = 'Staging_Premiums'         
                AND (ISNULL(@Severity, '') = '' OR r.Severity = @Severity)         
                -- [SEARCH FILTER]
                AND (
                    ISNULL(@Search, '') = '' 
                    OR (
                        (@SearchColumn IS NULL OR @SearchColumn = '') AND (ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%' OR r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'ErrorMessage' AND ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'TargetColumn' AND r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'StagingTableName' AND e.StagingTableName LIKE '%' + @Search + '%')
                    )
                )
        
            UNION ALL         
        
            SELECT          
                e.StagingRowErrorId, e.StagingTableName, e.StagingRowId, st.RowNumber,         
                ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn, ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage, r.Severity, r.IsCorrectable, r.HelpText, e.Status         
            FROM StagingRowErrors e         
            JOIN ValidationRules r ON e.RuleId = r.RuleId         
            JOIN Staging_Employees st ON e.StagingRowId = st.StagingEmployeeId         
            WHERE e.FileLogId = @FileLogId          
                AND e.StagingTableName = 'Staging_Employees'         
                AND (ISNULL(@Severity, '') = '' OR r.Severity = @Severity)         
                -- [SEARCH FILTER]
                AND (
                    ISNULL(@Search, '') = '' 
                    OR (
                        (@SearchColumn IS NULL OR @SearchColumn = '') AND (ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%' OR r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'ErrorMessage' AND ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'TargetColumn' AND r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'StagingTableName' AND e.StagingTableName LIKE '%' + @Search + '%')
                    )
                )
        
            UNION ALL         
        
            SELECT          
                e.StagingRowErrorId, e.StagingTableName, e.StagingRowId, st.RowNumber,         
                ISNULL(e.ColumnName, r.TargetColumn) AS TargetColumn, ISNULL(e.Detail, r.ErrorMessage) AS ErrorMessage, r.Severity, r.IsCorrectable, r.HelpText, e.Status         
            FROM StagingRowErrors e         
            JOIN ValidationRules r ON e.RuleId = r.RuleId         
            JOIN Staging_Dependents st ON e.StagingRowId = st.StagingDependentId         
            WHERE e.FileLogId = @FileLogId          
                AND e.StagingTableName = 'Staging_Dependents'         
                AND (ISNULL(@Severity, '') = '' OR r.Severity = @Severity)
                -- [SEARCH FILTER]
                AND (
                    ISNULL(@Search, '') = '' 
                    OR (
                        (@SearchColumn IS NULL OR @SearchColumn = '') AND (ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%' OR r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'ErrorMessage' AND ISNULL(e.Detail, r.ErrorMessage) LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'TargetColumn' AND r.TargetColumn LIKE '%' + @Search + '%')
                        OR (@SearchColumn = 'StagingTableName' AND e.StagingTableName LIKE '%' + @Search + '%')
                    )
                );  
        END  
        
        -- Get record count         
        SELECT @RecordCount = COUNT(*) FROM #AllErrors;         
        SELECT @TotalErrorsCount = @RecordCount;  
          
        -- Get severity counts (filtered by @Severity if provided)  
        IF @Severity = 'Employer Action'  
        BEGIN  
            SELECT @EmployerActionCount = @RecordCount;  
        END  
        ELSE  
        BEGIN  
            SELECT @ErrorCount = COUNT(*) FROM #AllErrors WHERE Severity = 'Error'    
                AND (ISNULL(@Severity, '') = '' OR @Severity = 'Error');  
            SELECT @WarningCount = COUNT(*) FROM #AllErrors WHERE Severity = 'Warning'    
                AND (ISNULL(@Severity, '') = '' OR @Severity = 'Warning');  
            SELECT @InformationCount = COUNT(*) FROM #AllErrors WHERE Severity = 'Information'    
                AND (ISNULL(@Severity, '') = '' OR @Severity = 'Information');  
            SELECT @EmployerActionCount = COUNT(*) FROM #AllErrors WHERE IsCorrectable = 0;  
        END  
          
        SET @StartPage = (@PageIndex - 1) * @PageSize + 1;         
        SET @EndPage = @PageIndex * @PageSize;         
        
        -- Get paginated results with sorting         
        SELECT          
            StagingRowErrorId,         
            StagingTableName,         
            StagingRowId,         
            RowNumber,         
            TargetColumn,         
            ErrorMessage,         
            Severity,         
            IsCorrectable,         
            HelpText,         
            Status         
        FROM #AllErrors         
        ORDER BY         
            CASE WHEN (@SortColumn = 0 AND @SortOrder = 'asc') THEN RowNumber END ASC,         
            CASE WHEN (@SortColumn = 0 AND @SortOrder = 'desc') THEN RowNumber END DESC,         
            CASE WHEN (@SortColumn = 1 AND @SortOrder = 'asc') THEN ErrorMessage END ASC,         
            CASE WHEN (@SortColumn = 1 AND @SortOrder = 'desc') THEN ErrorMessage END DESC,         
            CASE WHEN (@SortColumn = 2 AND @SortOrder = 'asc') THEN Severity END ASC,         
            CASE WHEN (@SortColumn = 2 AND @SortOrder = 'desc') THEN Severity END DESC         
        OFFSET @PageSize * (@PageIndex - 1) ROWS         
        FETCH NEXT @PageSize ROWS ONLY;         
        
        -- Return pagination metadata and counts         
        SELECT        
            @RecordCount AS [RowCount],         
            @StartPage AS StartPage,         
            CASE WHEN @EndPage > @RecordCount THEN @RecordCount ELSE @EndPage END AS EndPage,         
            @PageIndex AS PageNumber,         
            @RecordCount AS TotalCount  
            
        SELECT    
            @ErrorCount AS ErrorCount,  
            @WarningCount AS WarningCount,  
            @InformationCount AS InformationCount,  
            @EmployerActionCount AS EmployerActionCount,  
            @TotalErrorsCount As TotalErrorsCount;  
        
        DROP TABLE #AllErrors;         
        COMMIT TRANSACTION;         
    END TRY         
    BEGIN CATCH         
        IF OBJECT_ID('tempdb..#AllErrors') IS NOT NULL         
            DROP TABLE #AllErrors;         
        
        INSERT INTO dbo.DB_Errors         
        VALUES         
        (         
            SUSER_SNAME(),         
            ERROR_NUMBER(),         
            ERROR_STATE(),         
            ERROR_SEVERITY(),         
            ERROR_LINE(),         
            ERROR_PROCEDURE(),         
            ERROR_MESSAGE(),         
            GETDATE()         
        );         
        
        IF (XACT_STATE()) = -1         
            ROLLBACK TRANSACTION;         
        
        IF (XACT_STATE()) = 1         
            COMMIT TRANSACTION;         
        
        THROW;         
    END CATCH         
END
GO

-- sp_GetValidationReport
CREATE OR ALTER PROCEDURE [dbo].[sp_GetValidationReport]
    @FileLogId INT,
    @TableName NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Sql NVARCHAR(MAX);
    DECLARE @Cols NVARCHAR(MAX);
    DECLARE @PKColumn NVARCHAR(100);

    -- 1. Determine the Primary Key Column
    SET @PKColumn = 'Staging' + REPLACE(REPLACE(@TableName, 'Staging_', ''), 's', '') + 'Id';

    -- 2. Get List of Columns
    SELECT @Cols = STRING_AGG(QUOTENAME(c.name), ', ') WITHIN GROUP (ORDER BY c.column_id)
    FROM sys.columns c
    INNER JOIN sys.tables t ON c.object_id = t.object_id
    WHERE t.name = @TableName
      AND c.name NOT IN ('FileLogId', 'IsValid', 'CreatedDate', 'ModifiedDate') 
      AND c.name <> @PKColumn; 

    -- 3. Build Query
    SET @Sql = N'
    SELECT 
        S.RowNumber,
        CASE 
            WHEN SUM(CASE WHEN R.Severity = ''Error'' THEN 1 ELSE 0 END) > 0 THEN ''Failed''
            WHEN SUM(CASE WHEN R.Severity = ''Warning'' THEN 1 ELSE 0 END) > 0 THEN ''Warning''
            ELSE ''Passed''
        END AS [Validation Status],
        ISNULL(STRING_AGG(CAST(R.ErrorMessage AS NVARCHAR(MAX)), '' | ''), '''') AS [Error Messages],
        
        -- NEW: Comma-separated lists of columns that have errors or warnings!
        ISNULL(STRING_AGG(CASE WHEN R.Severity = ''Error'' THEN CAST(R.TargetColumn AS NVARCHAR(MAX)) ELSE NULL END, '',''), '''') AS [ErrorColumns],
        ISNULL(STRING_AGG(CASE WHEN R.Severity = ''Warning'' THEN CAST(R.TargetColumn AS NVARCHAR(MAX)) ELSE NULL END, '',''), '''') AS [WarningColumns],
        
        ' + @Cols + '
    FROM ' + @TableName + ' S
    LEFT JOIN (
        SELECT E.StagingRowId, ISNULL(E.Detail, V.ErrorMessage) AS ErrorMessage, V.Severity, ISNULL(E.ColumnName, V.TargetColumn) AS TargetColumn
        FROM StagingRowErrors E
        JOIN ValidationRules V ON E.RuleId = V.RuleId
        WHERE E.FileLogId = @FileLogId AND E.StagingTableName = ''' + @TableName + '''
    ) R ON S.' + @PKColumn + ' = R.StagingRowId 
    WHERE S.FileLogId = @FileLogId
    GROUP BY S.RowNumber, S.' + @PKColumn + ', ' + @Cols + '
    ORDER BY S.RowNumber;';

    -- 4. Execute
    EXEC sp_executesql @Sql, N'@FileLogId INT', @FileLogId;
END
GO

PRINT '055: validation rules fixed (lookups, rule 19, affordability bands, ZIP by state); auto-corrections recorded.';
GO
