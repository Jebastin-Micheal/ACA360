window.initializeFlaggedGrid = function (root) {
        root = root || document.getElementById('ffg-root');
        if (!root) throw new Error('The #ffg-root element was not found.');
        if (root._flagGrid) return root._flagGrid;
        if (typeof window.disposeFlaggedGrid === 'function') window.disposeFlaggedGrid();

        // definitions and sourceRows are now handled dynamically via AJAX
        let rows = [];
        const columnFields = [null, "FullName", "FirstName", "LastName", "SSN", "Birthday", "Address", "Address2", "City", "State", "Zip", "$hire", "HireDate", "HireEndDate", "Email", "$enrollment", "PlanId", "PlanName", "IsUnionMember", "Union_ContributionStartDate", "Union_ContributionEndDate", "CoverageOfferDate", "IsMedicalEnrolled", "Medical_CoverageStartDate", "Medical_CoverageEndDate", "IsCOBRAEnrolled", "COBRA_StartDate", "COBRA_EndDate", "IsRetireeEnrolled", "Retiree_StartDate", "Retiree_EndDate", "$status", "Status", "StatusStartDate", "StatusEndDate", "$payroll", "PayPeriodStartDate", "PayPeriodEndDate", "PayPeriodTotalHours", "PayPeriodSalaryAmount", "PayPeriodHourlyAmount", "PayPeriodAdditional", "Note", "$dependent", "DependentFirstName", "DependentMiddleName", "DependentLastName", "DependentSSN", "DependentBirthday", "DependentCoverageStartDate", "DependentCoverageEndDate", "DependentSuffix"];
        const mappings = {"FirstName": {"table": "Employee", "key": "EmployeeId"}, "LastName": {"table": "Employee", "key": "EmployeeId"}, "SSN": {"table": "Employee", "key": "EmployeeId"}, "Address": {"table": "Employee", "key": "EmployeeId"}, "Address2": {"table": "Employee", "key": "EmployeeId"}, "City": {"table": "Employee", "key": "EmployeeId"}, "Zip": {"table": "Employee", "key": "EmployeeId"}, "Email": {"table": "Employee", "key": "EmployeeId"}, "Birthday": {"table": "Employee", "key": "EmployeeId"}, "State": {"table": "Employee", "key": "EmployeeId"}, "HireDate": {"table": "EmployeeHireSpan", "key": "HireSpanId"}, "HireEndDate": {"table": "EmployeeHireSpan", "key": "HireSpanId"}, "Union_ContributionStartDate": {"table": "EmployeeEnrollment", "key": "EnrollmentId"}, "Union_ContributionEndDate": {"table": "EmployeeEnrollment", "key": "EnrollmentId"}, "CoverageOfferDate": {"table": "EmployeeEnrollment", "key": "EnrollmentId"}, "Medical_CoverageStartDate": {"table": "EmployeeEnrollment", "key": "EnrollmentId"}, "Medical_CoverageEndDate": {"table": "EmployeeEnrollment", "key": "EnrollmentId"}, "COBRA_StartDate": {"table": "EmployeeEnrollment", "key": "EnrollmentId"}, "COBRA_EndDate": {"table": "EmployeeEnrollment", "key": "EnrollmentId"}, "Retiree_StartDate": {"table": "EmployeeEnrollment", "key": "EnrollmentId"}, "Retiree_EndDate": {"table": "EmployeeEnrollment", "key": "EnrollmentId"}, "Status": {"table": "EmployeeStatus", "key": "StatusId"}, "StatusStartDate": {"table": "EmployeeStatus", "key": "StatusId"}, "StatusEndDate": {"table": "EmployeeStatus", "key": "StatusId"}, "PayPeriodStartDate": {"table": "EmployeePayroll", "key": "PayrollId"}, "PayPeriodEndDate": {"table": "EmployeePayroll", "key": "PayrollId"}, "PayPeriodTotalHours": {"table": "EmployeePayroll", "key": "PayrollId"}, "PayPeriodSalaryAmount": {"table": "EmployeePayroll", "key": "PayrollId"}, "PayPeriodHourlyAmount": {"table": "EmployeePayroll", "key": "PayrollId"}, "PayPeriodAdditional": {"table": "EmployeePayroll", "key": "PayrollId"}, "Note": {"table": "EmployeePayroll", "key": "PayrollId"}, "DependentFirstName": {"table": "CoveredIndividual", "key": "CoveredIndividualId"}, "DependentMiddleName": {"table": "CoveredIndividual", "key": "CoveredIndividualId"}, "DependentLastName": {"table": "CoveredIndividual", "key": "CoveredIndividualId"}, "DependentSSN": {"table": "CoveredIndividual", "key": "CoveredIndividualId"}, "DependentBirthday": {"table": "CoveredIndividual", "key": "CoveredIndividualId"}, "DependentCoverageStartDate": {"table": "CoveredIndividual", "key": "CoveredIndividualId"}, "DependentCoverageEndDate": {"table": "CoveredIndividual", "key": "CoveredIndividualId"}, "DependentSuffix": {"table": "CoveredIndividual", "key": "CoveredIndividualId"}};
        const groups = {"hire": {"table": "EmployeeHireSpan", "key": "HireSpanId", "json": "HireSpansJson", "label": "Hire record", "sort": "startDate", "fields": ["HireDate", "HireEndDate"]}, "enrollment": {"table": "EmployeeEnrollment", "key": "EnrollmentId", "json": "EnrollmentsJson", "label": "Enrollment", "sort": "coverageStartDate", "fields": ["PlanId", "PlanName", "IsUnionMember", "IsMedicalEnrolled", "IsCOBRAEnrolled", "IsRetireeEnrolled", "Union_ContributionStartDate", "Union_ContributionEndDate", "CoverageOfferDate", "Medical_CoverageStartDate", "Medical_CoverageEndDate", "COBRA_StartDate", "COBRA_EndDate", "Retiree_StartDate", "Retiree_EndDate"]}, "status": {"table": "EmployeeStatus", "key": "StatusId", "json": "StatusesJson", "label": "Status record", "sort": "startDate", "fields": ["Status", "StatusStartDate", "StatusEndDate"]}, "payroll": {"table": "EmployeePayroll", "key": "PayrollId", "json": "PayrollsJson", "label": "Pay period", "sort": "payPeriodStartDate", "fields": ["PayPeriodStartDate", "PayPeriodEndDate", "PayPeriodTotalHours", "PayPeriodSalaryAmount", "PayPeriodHourlyAmount", "PayPeriodAdditional", "Note"]}, "dependent": {"table": "CoveredIndividual", "key": "CoveredIndividualId", "json": "DependentsJson", "label": "Dependent", "sort": "birthday", "fields": ["DependentFirstName", "DependentMiddleName", "DependentLastName", "DependentSSN", "DependentBirthday", "DependentCoverageStartDate", "DependentCoverageEndDate", "DependentSuffix"]}};
        const dateFields = new Set(["Birthday", "HireDate", "HireEndDate", "Union_ContributionStartDate", "Union_ContributionEndDate", "CoverageOfferDate", "Medical_CoverageStartDate", "Medical_CoverageEndDate", "COBRA_StartDate", "COBRA_EndDate", "Retiree_StartDate", "Retiree_EndDate", "StatusStartDate", "StatusEndDate", "PayPeriodStartDate", "PayPeriodEndDate", "DependentBirthday", "DependentCoverageStartDate", "DependentCoverageEndDate", "CoverageStart"]);
        const booleanFields = new Set(['IsUnionMember','IsMedicalEnrolled','IsCOBRAEnrolled','IsRetireeEnrolled']);
        const identityFields = ['EmployeeId','EmployeeCodeId','HireSpanId','EnrollmentId','StatusId','PayrollId','CoveredIndividualId'];
        const fieldGroup = {};
        Object.entries(groups).forEach(([name,g]) => g.fields.forEach(field => fieldGroup[field] = name));
        const tabOrder=['employee','hire','status','payroll','enrollment','dependent'];
        const tabLabels = { employee: 'Employee Info', hire: 'Hire', status: 'Status', payroll: 'Payroll', enrollment: 'Enrollment', dependent:'Dependent'};
        const severityOrder=['critical','warning','info'];
        const hiddenFields = new Set(['FullName', 'PlanId']);
        const statusLabels = { '0': 'FULL-TIME', '1': 'PART-TIME' };

        const $grid = $(root).find('#ffGrid');
        if (!$grid.length || !$.fn.DataTable) throw new Error('Load jQuery and DataTables before flag-fix.js.');
        const $root = $grid.closest('.flag-fix-modal-container');
        const $button = $root.find('#btnSaveAllBatch');
        const $flag = $root.find('#filterFlag');
        const $severity = $root.find('#filterSeverity');
        const $pageSize = $root.find('#ffPageSize');
        let activeTab = 'employee';
        const $modal = $root.closest('.modal');
        const changedCells = new Map();
        const baseline = new Map();
        const defsByCode = new Map(); // Populated from AJAX

        let saving = false;
        let committing = false;
        let disposed = false;
        let table;
        let activeRequest = null, requestSequence = 0, definitionsLoaded = false;
        let lastTotal = 0, lastFiltered = 0, resizeTimer;
        let countsRequest=null,countsSequence=0,countsStarted=false,countsLoaded=false;
        let revealSelectedFlag=false;
        const config = { employerIds: root.dataset.employerIds, filingYear: Number(root.dataset.filingYear),
            getUrl: root.dataset.getUrl, saveUrl: root.dataset.saveUrl, countsUrl: root.dataset.countsUrl };
        const token = $root.find('input[name="__RequestVerificationToken"]').val();
        if (!token || !config.filingYear || !config.getUrl || !config.saveUrl || !config.countsUrl) throw new Error('Grid year or security token is missing.');
        const selectedChildren = new Map();
        const numericFields = new Set(['PayPeriodTotalHours','PayPeriodSalaryAmount','PayPeriodHourlyAmount','PayPeriodAdditional']);
        const datePairs = [['HireDate','HireEndDate'],['Union_ContributionStartDate','Union_ContributionEndDate'],
            ['Medical_CoverageStartDate','Medical_CoverageEndDate'],['COBRA_StartDate','COBRA_EndDate'],
            ['Retiree_StartDate','Retiree_EndDate'],['StatusStartDate','StatusEndDate'],
            ['PayPeriodStartDate','PayPeriodEndDate'],['DependentCoverageStartDate','DependentCoverageEndDate']];

        function text(value) { return value == null ? '' : String(value); }
        function escapeHtml(value) { return text(value).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c])); }
        function codes(value) { return [...new Set(text(value).split(',').map(s=>s.trim().toLowerCase()).filter(Boolean))]; }
        function notify(kind,message) { if (window.toastr?.[kind]) window.toastr[kind](message); else window.alert(message); }
        function display(field,value) { return booleanFields.has(field) ? (value == null ? '' : (value===true || value===1 ? 'Yes':'No')) : text(value); }
        function display(field, value) {
           if (field === 'Status') return value == null || value === '' ? '' : statusLabels[text(value)] || 'Unknown status';
            return booleanFields.has(field) ? (value == null ? '' : (value === true || value === 1 ? 'Yes' : 'No')) : text(value);
        }
        function fieldVisible(field, index) {
             if (hiddenFields.has(field)) return false;
             if (index < 6) return true; const group = field?.startsWith('$') ? field.slice(1) : fieldGroup[field];
            return activeTab === 'employee' ? !group : group === activeTab;
        }
        function parseChildren(value) {
            if (Array.isArray(value)) return value;
            if (!value) return [];
            const parsed = JSON.parse(value);
            if (!Array.isArray(parsed)) throw new Error('A child collection is not a JSON array. Check the updated GET/model.');
            return parsed;
        }

        function selectChild(row,name,id) {
            const g=groups[name];
            const child=row._childById?.[name]?.get(Number(id)) || row._children[name].find(c=>Number(c[g.key])===Number(id)) || null;
            row[g.key]=child ? Number(child[g.key]) : null;
            g.fields.forEach(field=>row[field]=child?.[field] ?? null);
        }

        function prepareRows(input) {
            const employees=new Map();
            input.forEach(source=>{
                if (!(Number(source.EmployeeCodeId)>0)) throw new Error('EmployeeCodeId is missing. Apply the updated GET and grid model.');
                let row=employees.get(Number(source.EmployeeCodeId));
                if (!row) { row={...source,_children:{},_childById:{}};Object.keys(groups).forEach(name=>{row._children[name]=[];row._childById[name]=new Map();});employees.set(Number(source.EmployeeCodeId),row); }
                if (row.EmployeeId!==source.EmployeeId) throw new Error('An employee code refers to conflicting employees. Check the GET result.');
                Object.entries(groups).forEach(([name,g])=>{
                    let children=parseChildren(source[g.json]);
                    if (!children.length && Number(source[g.key])>0) {
                        const child={[g.key]:Number(source[g.key])};g.fields.forEach(field=>child[field]=source[field]??null);children=[child];
                    }
                    children.forEach(child=>{
                        if (!(Number(child[g.key])>0)) return;
                        const key=Number(child[g.key]),existing=row._childById[name].get(key);
                        if (!existing) {const copy={...child,[g.key]:key};row._children[name].push(copy);row._childById[name].set(key,copy);}
                        else if(g.fields.some(field=>text(existing[field])!==text(child[field]))) throw new Error('Conflicting child records were returned.');
                    });
                });
                row.AllFlagCodes=[...new Set([...codes(row.AllFlagCodes),...codes(source.AllFlagCodes)])].join(',');
            });
            const prepared=Array.from(employees.values());
            prepared.forEach(row=>Object.entries(groups).forEach(([name,g])=>{
                selectChild(row,name,row._children[name][0]?.[g.key]);delete row[g.json];
            }));
            return prepared;
        }

        const canonical=new Map(columnFields.filter(field=>field && !field.startsWith('$')).map(field=>[field.toLowerCase(),field]));
        const aliases={dob:'Birthday',dateofbirth:'Birthday',birthdate:'Birthday',address1:'Address',zipcode:'Zip',stateid:'State',
            hire_startdate:'HireDate',hire_enddate:'HireEndDate',contribution_startdate:'Union_ContributionStartDate',contribution_enddate:'Union_ContributionEndDate',
            dependentbirthdate:'DependentBirthday',dependentdateofbirth:'DependentBirthday',
            coveragestartdate:'Medical_CoverageStartDate',coverageenddate:'Medical_CoverageEndDate'};
        // Existing rules use Employee as a logical scope for these explicit child aliases.
        // Generic child column names (startDate, endDate, SSN, etc.) still require their own table.
        const employeeScopeFields=new Set(['hire','status','payroll','enrollment'].flatMap(name=>groups[name].fields));
        const employeeScopeAliases=new Set(['hire_startdate','hire_enddate','contribution_startdate','contribution_enddate']);

        function resolveField(raw,definition) {
            const value=text(raw).trim().replace(/[\[\]]/g,'').split('.').pop().toLowerCase();
            const target=text(definition?.TargetTable).trim().replace(/[\[\]]/g,'').split('.').pop().toLowerCase();
            const tableAliases={
                employeehirespan:{startdate:'HireDate',enddate:'HireEndDate'},
                employeestatus:{startdate:'StatusStartDate',enddate:'StatusEndDate'},
                employeeenrollment:{contributionstartdate:'Union_ContributionStartDate',contributionenddate:'Union_ContributionEndDate',
                    coveragestartdate:'Medical_CoverageStartDate',coverageenddate:'Medical_CoverageEndDate',
                    cobrastartdate:'COBRA_StartDate',cobraenddate:'COBRA_EndDate',retireestartdate:'Retiree_StartDate',retireeenddate:'Retiree_EndDate'},
                coveredindividual:{firstname:'DependentFirstName',middlename:'DependentMiddleName',lastname:'DependentLastName',ssn:'DependentSSN',
                    birthday:'DependentBirthday',dob:'DependentBirthday',birthdate:'DependentBirthday',dateofbirth:'DependentBirthday',
                    coveragestartdate:'DependentCoverageStartDate',coverageenddate:'DependentCoverageEndDate',suffix:'DependentSuffix'}
            };
            const field=tableAliases[target]?.[value] || canonical.get(value) || aliases[value] || null;
            const owner=fieldGroup[field]?groups[fieldGroup[field]].table:(field?'Employee':null);
            const employeeScope=target==='employee' && employeeScopeFields.has(field) &&
                (canonical.get(value)===field || employeeScopeAliases.has(value));
            if(field && target && owner?.toLowerCase()!==target && !employeeScope) return null;
            return field;
        }

        function definitionFields(definition) {
            return [...new Set(text(definition?.TargetField).split(',').map(raw=>resolveField(raw,definition)).filter(Boolean))];
        }
        function targetTabs(definition) {
            const targets=new Set(definitionFields(definition).map(field=>fieldGroup[field] || 'employee'));
            return tabOrder.filter(name=>targets.has(name));
        }
        function tabForFlag(definition) {
            // Identity fields stay visible on every sheet; show a child sheet first when needed.
            const targets=targetTabs(definition);
            return targets.find(name=>name!=='employee') || targets[0] || 'employee';
        }
        function prepareFlags(row) {
            row._codes=codes([row.AllFlagCodes,...severityOrder.map(kind=>row[kind[0].toUpperCase()+kind.slice(1)+'FlagCodes'])].join(','));
            row._severityKinds=new Set();
            row._flagFields={critical:new Set(),warning:new Set(),info:new Set()};
            row._flags=[];
            row._codes.forEach(code=>{
                if (code==='0.0') return;
                const definition=defsByCode.get(code);
                const fallback=severityOrder.find(kind=>codes(row[kind[0].toUpperCase()+kind.slice(1)+'FlagCodes']).includes(code));
                const severity=text(definition?.Severity || fallback).trim().toLowerCase();
                const fields=definitionFields(definition);
                row._flags.push({...definition,FlagCode:definition?.FlagCode || code,
                    Description:definition?.Description || 'Flag description is unavailable.',Severity:severity,fields});
                if (!row._flagFields[severity]) return;
                row._severityKinds.add(severity);
                fields.forEach(field=>row._flagFields[severity].add(field));
            });
        }

        function severityOf(row,field) { return severityOrder.find(kind=>row._flagFields[kind].has(field)) || null; }
        function flagsFor(row,field=null) {
            const selected=text($flag.val()).trim().toLowerCase(),severity=$severity.val();
            return (row._flags || []).filter(flag=>(!selected || text(flag.FlagCode).toLowerCase()===selected) &&
                (!severityOrder.includes(severity) || flag.Severity === severity) && (!field || flag.fields.includes(field) || (field === 'PlanName' && flag.fields.includes('PlanId'))));
        }
        function highlightSeverity(row,field) {
            const flags=flagsFor(row,field==='FullName'?null:field);
            return severityOrder.find(kind=>flags.some(flag=>flag.Severity===kind)) || null;
        }
        function flagSummary(flag) {
            return `${flag.FlagId==null?'':flag.FlagId+' ' }(${flag.FlagCode}) · ${flag.Severity || 'Flag'} — ${flag.Description}`;
        }
        function flagTooltip(flags) { return flags.map(flagSummary).join('\n'); }
        function descriptionHtml(flags) {
            return flags.map(flag=>{
                const severity=text(flag.Severity).trim().toLowerCase();
                const badge=severityOrder.includes(severity)?` ff-description-${severity}`:'';
                const tabs=targetTabs(flag);
                const where=tabs.length?tabs.map(name=>tabLabels[name]).join(', '):'No matching field is shown in this grid; review the flag in the employee details.';
                return `<div class="ff-description-item"><span class="ff-description-badge${badge}">${escapeHtml(flag.FlagCode)} · ${escapeHtml(severity || 'Flag')}</span> ` +
                    `${escapeHtml(flag.Description)} <span class="ff-description-location">${escapeHtml(where)}</span></div>`;
            }).join('');
        }
        function updateFlagDetails(row=null,field=null) {
            const definition=defsByCode.get(text($flag.val()).trim().toLowerCase());
            let flags=row?flagsFor(row,field):definition?[definition]:[];
            if(row && !flags.length) flags=flagsFor(row);
            $root.find('#ffFlagDetailsHeading').text(row ? 'Row flag details' : definition ? 'Selected flag' : 'Flag details');
            $root.find('#ffFlagDetailsText').html(flags.length?descriptionHtml(flags):
                'Hover a highlighted field or row to see its flags. Click a field or row to read the descriptions here.');
            $root.find('#ffResetDetails').prop('hidden',!row);
        }
        function markFlagTabs() {
            const targets=targetTabs(defsByCode.get(text($flag.val()).trim().toLowerCase()));
            $root.find('[role="tab"]').each(function(){
                const related=targets.includes(this.dataset.tab);
                $(this).toggleClass('ff-tab-has-flag',related).attr('title',related?'Selected flag has fields in this tab':'');
            });
        }
        function canEdit(row,field) { const m=mappings[field];return !!m && Number(row[m.key])>0 && !!severityOf(row,field); }
        function cellKey(field,id) { return JSON.stringify([mappings[field].table,Number(id),field]); }
        function selectedKey(row,field) { return mappings[field] ? cellKey(field,row[mappings[field].key]) : ''; }

        function captureBaseline() {
            for (const key of baseline.keys()) if (!changedCells.has(key)) baseline.delete(key);
            rows.forEach(row=>Object.keys(mappings).forEach(field=>{
                const name=fieldGroup[field];
                if (!name) rememberBaseline(cellKey(field,row.EmployeeId),row[field]);
                else row._children[name].forEach(child=>rememberBaseline(cellKey(field,child[groups[name].key]),child[field]));
            }));
        }

        function rememberBaseline(key, value) {
            if (!changedCells.has(key)) baseline.set(key,text(value));
        }
        function overlayEdits() {
            rows.forEach(row => {
                changedCells.forEach(change => {
                    const map=mappings[change.field], name=fieldGroup[change.field];
                    if (!name && Number(row.EmployeeId)===Number(change.ids.EmployeeId)) row[change.field]=change.value;
                    if (name) {
                        const child=row._childById[name].get(Number(change.ids[map.key]));
                        if (child) child[change.field]=change.value;
                    }
                });
                Object.entries(groups).forEach(([name,g]) => selectChild(row,name,
                    selectedChildren.get(`${row.EmployeeCodeId}:${name}`) ?? row._children[name][0]?.[g.key]));
                row.FullName=[row.FirstName,row.LastName].filter(Boolean).join(' ');
            });
        }

        function countText(value) { return value==null?'…':Number(value).toLocaleString('en-US'); }
        function flagOptionText(def,totalRecords) {
            return def ? `${text(def.Description)} — ${countText(def.UsageCount)} (${countText(def.OccurrenceCount)})` : `All flags — ${countText(totalRecords || 0)}`;
        }
        function formatFlagOption(option) {
            if(option.loading) return option.text;
            const def=defsByCode.get(text(option.id).trim().toLowerCase());
            if(option.id && !def) return option.text;
            // Return DOM nodes: descriptions are text, never executable Select2 markup.
            const counts=$('<span class="ff-flag-counts">')
                .append($('<strong class="ff-flag-count">').attr('title','Employee records').text(countText(def?def.UsageCount:lastTotal)));
            
            return $('<span class="ff-flag-option">').attr('data-flag-code',option.id || '')
                .append($('<span class="ff-flag-option-label">').text(def ? text(def.Description) : 'All flags'))
                .append(counts);
        }
        function initializeFlagSelect() {
            if(!$.fn.select2) throw new Error('Load the existing Select2 script before opening the flag grid.');
            if($flag.hasClass('select2-hidden-accessible')) $flag.select2('destroy');
            $flag.select2({width:'100%',dropdownParent:$root,templateResult:formatFlagOption,templateSelection:formatFlagOption});
        }
        function fillFlagOptions(serverDefs, totalRecords) {
            const currentVal = $flag.val();
            const definitions=[...serverDefs].filter(def=>def.FlagCode!=='0.0')
                .sort((a,b)=>String(a.FlagCode).localeCompare(String(b.FlagCode),undefined,{numeric:true}));
            const choices=[{value:'',label:flagOptionText(null,totalRecords)},...definitions.map(def=>({value:String(def.FlagCode),label:flagOptionText(def,totalRecords)}))];
            const existing=$flag.find('option').toArray();
            // Count refreshes must preserve option nodes: an already-open Select2 result refers to them.
            if(existing.length===choices.length && existing.every((option,index)=>option.value===choices[index].value))
                existing.forEach((option,index)=>option.textContent=choices[index].label);
            else {
                $flag.empty();choices.forEach(option=>$flag.append($('<option>').val(option.value).text(option.label)));
            }
            $flag.val(currentVal || '').trigger('change.select2');
            $root.find('.select2-results__option .ff-flag-option').each(function(){
                const def=defsByCode.get(text(this.dataset.flagCode).trim().toLowerCase());
                $(this).find('.ff-flag-count:not(.ff-occurrence-count)').text(countText(def?def.UsageCount:lastTotal));
                $(this).find('.ff-occurrence-count').text('('+countText(def?.OccurrenceCount)+')');
            });
            $flag.attr('title','Blue: employee records. Purple in parentheses: flag occurrences, including separate months. Counts cover the selected employers and filing year.');
        }

        async function loadFlagCounts() {
            if(disposed || countsRequest || countsLoaded) return;
            countsStarted=true;const sequence=++countsSequence;countsRequest=new AbortController();
            $root.find('#ffRetryCounts').prop('hidden',true);$root.find('#ffCountsStatus').text('Loading flag counts…');
            try {
                const url=new URL(config.countsUrl,window.location.origin);
                url.searchParams.set('employerIds',config.employerIds || '');url.searchParams.set('filingYear',String(config.filingYear));
                const response=await fetch(url,{credentials:'same-origin',cache:'no-store',signal:countsRequest.signal});
                const json=await response.json().catch(()=>null);
                if(!response.ok || !Array.isArray(json?.flagDefs)) throw new Error('Flag counts could not be loaded.');
                if(disposed || sequence!==countsSequence) return;
                json.flagDefs.forEach(def=>{
                    const key=text(def.FlagCode).trim().toLowerCase();
                    if(defsByCode.has(key)) {defsByCode.get(key).UsageCount=def.UsageCount;defsByCode.get(key).OccurrenceCount=def.OccurrenceCount;}
                });
                countsLoaded=true;fillFlagOptions([...defsByCode.values()],lastTotal);$root.find('#ffCountsStatus').text('');
            } catch(error) {
                if(disposed || sequence!==countsSequence || error.name==='AbortError') return;
                $root.find('#ffCountsStatus').text('Flag counts are unavailable. Editing is still available.');
                $root.find('#ffRetryCounts').prop('hidden',false);
            } finally {if(sequence===countsSequence) countsRequest=null;}
        }

        function updateChangeUi() {
            $root.find('#changeCount').text(changedCells.size);
            $button.prop('disabled',saving || !!activeRequest || changedCells.size===0 || $grid.find('[aria-invalid="true"]').length>0);
        }

        function recordLabel(name,child,index) {
            if (name === 'dependent') return `${index + 1}. ${[child.DependentFirstName, child.DependentLastName].filter(Boolean).join(' ') || 'Dependent'}`;
            const start={hire:'HireDate',enrollment:'Medical_CoverageStartDate',status:'StatusStartDate',payroll:'PayPeriodStartDate'}[name];
            const end={hire:'HireEndDate',enrollment:'Medical_CoverageEndDate',status:'StatusEndDate',payroll:'PayPeriodEndDate'}[name];
            const detail = name === 'status' ? display('Status', child.Status) : name === 'enrollment' ? text(child.PlanName) : '';
            return `${index + 1}. ${detail ? detail + ' · ' : ''}${text(child[start]) || 'No start date'}${end && child[end] ? ' to ' + child[end] : ''}`;
        }

        function renderRecord(row,name,type) {
            const g=groups[name],children=row._children[name];
            if(type!=='display') return children.map((c,i)=>recordLabel(name,c,i)).join(' ');
            if(!children.length) return '<span class="ff-record-empty">No records</span>';
            return `<select class="ff-record-select" data-group="${name}" aria-label="${g.label} for employee ${escapeHtml(row.EmployeeNo)}" ${saving?'disabled':''}>`+
                children.map((child,index)=>`<option value="${child[g.key]}" ${Number(row[g.key])===Number(child[g.key])?'selected':''}>${escapeHtml(recordLabel(name,child,index))}</option>`).join('')+'</select>';
        }

        const calendarIcon='<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" aria-hidden="true"><rect x="3" y="5" width="18" height="16" rx="2"></rect><path d="M16 3v4M8 3v4M3 11h18M8 15h2M14 15h2"></path></svg>';
        function renderValue(row,field,type) {
            if(field?.startsWith('$')) return renderRecord(row,field.slice(1),type);
            const value=row[field];
            if (type !== 'display') return display(field, value);
            if (field === 'Status') {
                if (!canEdit(row, field)) return escapeHtml(display(field, value));
                const selected = text(value);
                const unknown = selected !== '' && !Object.hasOwn(statusLabels, selected);
                return `<select class="ff-status-select" data-field="Status" aria-label="Employment status" ${saving ? 'disabled' : ''}>` +
                    `<option value="" ${selected === '' ? 'selected' : ''}>Select status</option>` +
                    (unknown ? `<option value="${escapeHtml(selected)}" selected disabled>Unknown status</option>` : '') +
                    Object.entries(statusLabels).map(([code, label]) => `<option value="${code}" ${selected === code ? 'selected' : ''}>${label}</option>`).join('') + '</select>';
            }
            if(dateFields.has(field)) {
                const enabled=canEdit(row,field) && !saving;
                return `<div class="ff-date-editor"><input type="date" class="ff-date-input" data-field="${field}" value="${escapeHtml(value)}" min="1000-01-01" max="9999-12-31" aria-label="${field}" ${enabled?'':'disabled'}>`+
                    `<button type="button" class="ff-date-button" aria-label="Choose ${field}" title="Choose date" ${enabled?'':'disabled'}>${calendarIcon}</button></div>`;
            }
            //if(field==='FullName') return `<strong>${escapeHtml(value)}</strong><div class="ff-row-severities">` +
            //    severityOrder.filter(kind=>flagsFor(row).some(flag=>flag.Severity===kind)).map(s=>`<span class="ff-description-${s}">${s}</span>`).join('') + '</div>';
            return escapeHtml(display(field,value));
        }

        function styleCell(td,row,field) {
            if(!td || !field || field.startsWith('$')) return;
            const severity = highlightSeverity(row, field), editable = canEdit(row, field);
            const textEditable = editable && !dateFields.has(field) && field !== 'Status';
            const flags=flagsFor(row,field==='FullName'?null:field);
            $(td).attr('data-field',field).attr('spellcheck','false')
                .attr('contenteditable', textEditable && !saving ? 'true' : 'false')
                .toggleClass('editable-cell', editable).toggleClass('ff-text-edit', textEditable)
                .toggleClass('cell-locked',!editable).toggleClass('flag-crit',severity==='critical')
                .toggleClass('flag-warn',severity==='warning').toggleClass('flag-info',severity==='info')
                .toggleClass('ff-flagged-cell',flags.length>0)
                .toggleClass('cell-edited',changedCells.has(selectedKey(row,field)));
            const hint=severity && mappings[field] && !Number(row[mappings[field].key])?
                'This child record is missing. Add it in the employee detail tab.':
                severity && !mappings[field] && field!=='FullName'?'This field is read-only in bulk fix. Use the employee detail form.':'';
            $(td).attr('title',flags.length?[flagTooltip(flags),hint].filter(Boolean).join('\n'):flagTooltip(flagsFor(row)) || display(field,row[field]));
            if(!editable && flags.length) $(td).attr('tabindex','0');else $(td).removeAttr('tabindex');
        }

        function applyRow(tr,row,displayNum,displayIndex) {
            $(tr).attr('title',flagTooltip(flagsFor(row)));
            $(tr).children('td').each(function () {
                const index = Number(this.dataset.ffColumn);
                if (index === 0) {
                    const number = (table ? table.page.info().start : 0) + displayIndex + 1;
                    const severity = highlightSeverity(row, null), flags = flagsFor(row);
                    $(this).text(number).attr('title', flagTooltip(flags))
                        .attr('aria-label', `Row ${number}: show flag details`)
                        .attr('tabindex', flags.length ? '0' : '-1').attr('contenteditable', 'false')
                        .toggleClass('ff-flagged-cell', flags.length > 0)
                        .toggleClass('flag-crit', severity === 'critical').toggleClass('flag-warn', severity === 'warning')
                        .toggleClass('flag-info', severity === 'info');
                } else styleCell(this, row, columnFields[index]);
            });
        }

        function refreshCell(index,field) {
            const column=columnFields.indexOf(field);
            if(column<0 || !table) return;
            const cell=table.cell(index,column);cell.invalidate('data');styleCell(cell.node(),rows[index],field);
        }

        function storeEdit(row,field,value,sourceNode=null) {
            if(saving || activeRequest || committing || !canEdit(row,field)) return false;
            if(numericFields.has(field) && value!=null && !/^[+-]?(?:\d+(?:\.\d*)?|\.\d+)$/.test(String(value))) {
                if(sourceNode) {sourceNode.setAttribute('aria-invalid','true');updateChangeUi();}
                else notify('error','Enter a decimal number without commas or currency symbols.');
                return false;
            }
            if (field === 'Status' && value != null && !Object.hasOwn(statusLabels, text(value))) { if (sourceNode) sourceNode.setAttribute('aria-invalid', 'true'); updateChangeUi(); return false; }
            if(sourceNode) sourceNode.removeAttribute('aria-invalid');
            if(text(row[field])===text(value)) {updateChangeUi();return false;}
            committing=true;
            try {
                const map=mappings[field],targetId=Number(row[map.key]),key=cellKey(field,targetId);
                if(baseline.get(key)===text(value)) changedCells.delete(key);
                else {
                    const ids={};identityFields.forEach(id=>ids[id]=null);
                    ids.EmployeeId=row.EmployeeId;ids.EmployeeCodeId=row.EmployeeCodeId;ids[map.key]=targetId;
                    changedCells.set(key,{ids,field,value});
                }
                const name=fieldGroup[field];
                rows.forEach((copy,index)=>{
                    if(!name) {
                        if(copy.EmployeeId!==row.EmployeeId) return;
                        copy[field]=value;
                        if(!sourceNode || table?.cell(index,columnFields.indexOf(field)).node()!==sourceNode) refreshCell(index,field);
                        else styleCell(sourceNode,copy,field);
                        if(field==='FirstName' || field==='LastName') {copy.FullName=[copy.FirstName,copy.LastName].filter(Boolean).join(' ');refreshCell(index,'FullName');}
                    } else {
                        const child=copy._childById[name].get(targetId);
                        if(!child) return;
                        child[field]=value;
                        if(Number(copy[map.key])===targetId) {copy[field]=value;
                        if(!sourceNode || table?.cell(index,columnFields.indexOf(field)).node()!==sourceNode) refreshCell(index,field);
                        else styleCell(sourceNode,copy,field);}
                        refreshCell(index,'$'+name);
                    }
                });
                updateChangeUi();
                // Reapply Edited using the filter or page controls; never fetch SQL per keystroke.
                return true;
            } finally {committing=false;}
        }

        function commitText(td) {
            const field=$(td).attr('data-field'),row=table.row($(td).closest('tr')).data();
            if(!row || !field) return;
            const value=$(td).text().trim();storeEdit(row,field,value===''?null:value,td);
            if(td.getAttribute('aria-invalid')==='true') return;
            refreshCell(table.row($(td).closest('tr')).index(),field);updateChangeUi();
        }

        function commitDate(input) {
            if(saving || activeRequest || committing || input.disabled) return;
            if(!input.validity.valid) {input.setAttribute('aria-invalid','true');updateChangeUi();return;}
            input.removeAttribute('aria-invalid');
            const field=input.dataset.field,row=table.row($(input).closest('tr')).data();
            if(row) storeEdit(row,field,input.value===''?null:input.value);
        }

        function buildEdits() {
            const grouped=new Map();
            changedCells.forEach(change=>{
                const key=JSON.stringify(identityFields.map(id=>change.ids[id]));
                if(!grouped.has(key)) grouped.set(key,{...change.ids,Changes:{}});
                grouped.get(key).Changes[change.field]=change.value;
            });
            return Array.from(grouped.values());
        }

        function countEmployees() {
            if (!table) return;
            const info = table.page.info();
            $root.find('#lblEmpCount').text(info.recordsDisplay);
            $root.find('.ff-legend-filter').each(function(){$(this).attr('aria-pressed',$(this).data('severity')===$severity.val()?'true':'false');});
        }

        if($.fn.dataTable.isDataTable($grid[0])) $grid.DataTable().destroy();
        initializeFlagSelect();

        const labels = {FullName:'Full Name',FirstName:'First Name',LastName:'Last Name',SSN:'SSN',Birthday:'Birthday',
            HireDate:'Hire Start',HireEndDate:'Hire End',DependentFirstName:'First Name',DependentMiddleName:'Middle Name',
            DependentLastName:'Last Name',DependentSSN:'SSN',DependentBirthday:'Birthday',DependentSuffix:'Suffix'};
        function columnLabel(field) {
            if(!field) return '#';
            if(field.startsWith('$')) return groups[field.slice(1)].label;
            return labels[field] || field.replace(/_/g,' ').replace(/([a-z])([A-Z])/g,'$1 $2');
        }
        //function fieldVisible(field,index) {
        //    if(index<6) return true;
        //    const group=field?.startsWith('$')?field.slice(1):fieldGroup[field];
        //    return activeTab==='employee'?!group:group===activeTab;
        //}
        function revealFlagField() {
            if(!revealSelectedFlag || !table || !rows.length) return;
            revealSelectedFlag=false;
            const fields = definitionFields(defsByCode.get(text($flag.val()).trim().toLowerCase())).map(field => field === 'PlanId' ? 'PlanName' : field);
            const field=columnFields.find((name,index)=>fields.includes(name) && fieldVisible(name,index) && index>=6);
            if(!field) return; // The affected identity field is already pinned and visible.
            const body=$root.find('.dataTables_scrollBody')[0],cell=table.cell(0,columnFields.indexOf(field)).node();
            if(!body || !cell) return;
            const bounds=body.getBoundingClientRect(),target=cell.getBoundingClientRect();
            const pins=[...body.querySelectorAll('tbody tr:first-child td.ff-pin')];
            const pinnedWidth=pins.reduce((width,node)=>width+(window.getComputedStyle(node).position==='sticky'?node.getBoundingClientRect().width:0),0);
            if(target.left<bounds.left+pinnedWidth || target.right>bounds.right)
                body.scrollLeft+=target.left-bounds.left-pinnedWidth-8;
        }
        $grid.find('thead tr').html(columnFields.map(field=>`<th>${escapeHtml(columnLabel(field))}</th>`).join(''));

        // Server-Side Initialization
        table = $grid.DataTable({
            serverSide: true,
            processing: true,
            ajax: function (d, callback) {
                const sequence=++requestSequence;
                const requestedTab=activeTab;
                if(activeRequest) activeRequest.abort();
                activeRequest=new AbortController();
                updateFlagDetails();markFlagTabs();
                $root.find('#ffGridWrap').attr('aria-busy','true');
                updateChangeUi();$root.find('#ffRetryPage').prop('hidden',true);
                const payload={draw:Number(d.draw),start:Number(d.start),length:Number(d.length),tab:activeTab,
                    employerIds:config.employerIds,filingYear:config.filingYear,
                    flagFilter:$flag.val() || '',severityFilter:$severity.val() || '',includeFlagDefs:!definitionsLoaded,
                    editedEmployeeCodeIds:[...new Set([...changedCells.values()].filter(c=>mappings[c.field].table!=='Employee').map(c=>c.ids.EmployeeCodeId))].join(','),
                    editedEmployeeIds:[...new Set([...changedCells.values()].filter(c=>mappings[c.field].table==='Employee').map(c=>c.ids.EmployeeId))].join(',')};
                fetch(config.getUrl,{method:'POST', credentials:'same-origin',
                    headers:{'Content-Type':'application/x-www-form-urlencoded; charset=UTF-8','RequestVerificationToken':token},
                    body:$.param(payload), signal:activeRequest.signal,cache:'no-store'})
                .then(async response=>{
                    const json=await response.json().catch(()=>null);
                    if(!response.ok || !json || json.error) throw new Error(json?.error || json?.message || `Grid load failed (HTTP ${response.status}).`);
                    if(disposed || sequence!==requestSequence) return;
                    if(json.tab!==requestedTab) throw new Error('Unexpected detail tab response. Update the controller and SQL together.');
                    if(Number(json.draw)!==Number(d.draw)) throw new Error('Unexpected grid response.');
                    lastTotal=json.recordsTotal; lastFiltered=json.recordsFiltered;
                    if(json.flagDefs!==null && json.flagDefs!==undefined) {
                        defsByCode.clear();
                        json.flagDefs.forEach(def=>defsByCode.set(text(def.FlagCode).trim().toLowerCase(),def));
                        fillFlagOptions(json.flagDefs,json.recordsTotal); definitionsLoaded=true;
                        markFlagTabs();updateFlagDetails();
                    }
                    rows=prepareRows(json.data || []); rows.forEach(prepareFlags);
                    captureBaseline(); overlayEdits();
                    $root.find('#ffGridError').prop('hidden',true).text('');
                    callback({...json,data:rows});
                    revealFlagField();
                    countEmployees();
                    // Start after this page has drawn; global counts must not delay usable rows.
                    if(!countsStarted) setTimeout(()=>{if(!disposed) loadFlagCounts();},0);
                }).catch(error=>{
                    if(error.name==='AbortError' || disposed || sequence!==requestSequence) return;
                    $root.find('#ffGridError').prop('hidden',false).text(error.message+' Unsaved edits are retained.');
                    // Complete processing even on HTTP/SQL errors. Do not clear staged edits.
                    $root.find('#ffRetryPage').prop('hidden',false);
                    rows=[]; callback({draw:Number(d.draw),recordsTotal:lastTotal,recordsFiltered:lastFiltered,data:[]});
                }).finally(()=>{if(sequence===requestSequence) {activeRequest=null;$root.find('#ffGridWrap').attr('aria-busy','false');updateChangeUi();}});
            },
            dom:'rt<"ff-dt-bottom"ip>',
            paging:true,
            pageLength:10,
            lengthChange:false,
            pagingType:'full_numbers',
            info:true,
            searching:false,
            ordering:false, // Ordered in SQL
            scrollX:true,
            scrollY:'calc(100vh - 390px)',
            scrollCollapse:true,
            autoWidth:false,
            deferRender:true,
            language:{
                paginate:{first:'First',previous:'Previous',next:'Next',last:'Last'},
                info:'Showing _START_–_END_ of _TOTAL_ employee records',
                infoEmpty:'No matching employee records',
                zeroRecords:'No records match these filters.'
            },
            columns:columnFields.map((field,index)=>index===0?{data:null,orderable:false,className:'text-center cell-locked ff-pin ff-pin-0',createdCell:td=>td.dataset.ffColumn=String(index),render:()=>''}:
                {data:field.startsWith('$')?null:field,defaultContent:'',orderable:false,
                    visible:fieldVisible(field,index),className:index<6?`ff-pin ff-pin-${index}`:'',
                    createdCell:td=>td.dataset.ffColumn=String(index),
                    width:index<6?[44,180,125,125,140,145][index]+'px':field.startsWith('$')?'230px':dateFields.has(field)?'155px':'160px',
                    render:(data,type,row)=>renderValue(row,field,type)}),
            rowCallback:applyRow
        });

        $grid.on('draw.dt.ffGrid',countEmployees);
        $grid.on('click.ffGrid focusin.ffGrid','tbody td',function(){
            const row=table.row($(this).closest('tr')).data();
            if(!row || activeRequest) return;
            const field=this.dataset.field;
            updateFlagDetails(row,field==='FullName'?null:field);
        });
        $grid.on('keydown.ffGrid','tbody td.ff-flagged-cell[contenteditable="false"]',function(event){
            if(event.target===this && (event.key==='Enter' || event.key===' ')) {event.preventDefault();$(this).trigger('click');}
        });
        $grid.on('input.ffGrid','tbody td.ff-text-edit',function(){
            if(activeRequest) return;
            const row=table.row($(this).closest('tr')).data(),field=this.dataset.field;
            if(!row || !field) return;
            const value=$(this).text().trim();storeEdit(row,field,value===''?null:value,this);
        });
        $grid.on('blur.ffGrid','tbody td.ff-text-edit',function(){commitText(this);});
        $grid.on('change.ffGrid', 'tbody .ff-date-input', function () { commitDate(this); });
        $grid.on('change.ffGrid', 'tbody .ff-status-select', function () {
          const row = table.row($(this).closest('tr')).data();
          if (!row) return;
          if (saving || activeRequest) { this.value = text(row.Status); return; }
          storeEdit(row, 'Status', this.value === '' ? null : this.value, this.closest('td'));
        });
        $grid.on('click.ffGrid','tbody .ff-date-button',function(){
            const input=this.parentElement.querySelector('.ff-date-input');if(!input || input.disabled) return;
            input.focus();try {if(typeof input.showPicker==='function') input.showPicker();else input.click();} catch(_) {input.focus();}
        });
        $grid.on('change.ffGrid','tbody .ff-record-select',function(){
            const name=this.dataset.group,index=table.row($(this).closest('tr')).index(),row=rows[index];
            if(!row) return;
            if(saving || activeRequest || !finishCurrentEdit()) {this.value=String(row[groups[name].key]);return;}
            selectedChildren.set(`${row.EmployeeCodeId}:${name}`,Number(this.value));
            selectChild(row,name,this.value);
            groups[name].fields.forEach(field=>refreshCell(index,field));
            refreshCell(index,'$'+name);
        });
        $grid.on('keydown.ffGrid','tbody td.ff-text-edit',function(event){
            if(event.key==='Enter'){event.preventDefault();this.blur();}
            if(event.key==='Tab'){
                event.preventDefault(); const controls = $grid.find('tbody td.ff-text-edit, tbody .ff-date-input:not(:disabled), tbody .ff-status-select:not(:disabled), tbody .ff-record-select:not(:disabled)').filter(':visible').toArray();
                const next=controls[controls.indexOf(this)+(event.shiftKey?-1:1)];this.blur();if(next && document.contains(next)) next.focus();
            }
        });
        $grid.on('paste.ffGrid','tbody td.ff-text-edit',function(event){
            const data=event.originalEvent.clipboardData;if(!data) return;event.preventDefault();
            const selection=window.getSelection();if(!selection?.rangeCount) return;
            const range=selection.getRangeAt(0);if(!this.contains(range.commonAncestorContainer)) return;
            range.deleteContents();const node=document.createTextNode(data.getData('text/plain'));range.insertNode(node);
            range.setStartAfter(node);range.collapse(true);selection.removeAllRanges();selection.addRange(range);
            $(this).trigger('input');
        });

        function finishCurrentEdit() {
            $grid.find('tbody td.ff-text-edit:focus').each(function(){commitText(this);});
            const invalid=$grid.find('tbody [aria-invalid="true"], tbody input:invalid').filter(':visible')[0];
            if(invalid) {invalid.focus();notify('error','Correct the invalid value before changing records or saving.');return false;}
            return true;
        }
        $pageSize.on('change.ffGrid',function(){
            if(saving || !finishCurrentEdit()) {this.value=String(table.page.len());return;}
            table.page.len(Number(this.value)).draw();
        });
        function activateTab(next) {
            if(!tabOrder.includes(next) || next===activeTab) return;
            activeTab=next;
            $root.find('[role="tab"]').attr('aria-selected','false').attr('tabindex','-1');
            $root.find('#ff-tab-'+next).attr('aria-selected','true').attr('tabindex','0');
            $root.find('#ffGridWrap').attr('aria-labelledby','ff-tab-'+next);
            columnFields.forEach((field,index)=>table.column(index).visible(fieldVisible(field,index),false));
            table.columns.adjust();
            // A previous sheet's horizontal position must not hide the new flag's fields.
            $root.find('.dataTables_scrollBody').scrollLeft(0);
        }
        $root.find('[role="tab"]').attr('tabindex','-1');$root.find('#ff-tab-employee').attr('tabindex','0');
        $root.find('[role="tab"]').on('click.ffGrid',function(){
            if(saving || this.dataset.tab===activeTab || !finishCurrentEdit()) return;
            activateTab(this.dataset.tab);
            revealSelectedFlag=!!$flag.val();
            table.ajax.reload(null,false); // Same employee page; SQL expands only this tab.
        }).on('keydown.ffGrid',function(event){
            if(!['ArrowLeft','ArrowRight','Home','End'].includes(event.key)) return;
            event.preventDefault();const tabs=$root.find('[role="tab"]').toArray(),index=tabs.indexOf(this);
            const next=event.key==='Home'?0:event.key==='End'?tabs.length-1:(index+(event.key==='ArrowRight'?1:-1)+tabs.length)%tabs.length;
            tabs[next].focus();tabs[next].click();
        });

        // One draw handles flag + sheet together, resetting the employee page to the first 10/25/etc.
        let acceptedFlag='',acceptedSeverity='';
        $flag.on('change.ffGrid',()=>{
            if(saving || !finishCurrentEdit()) {$flag.val(acceptedFlag).trigger('change.select2');return;}
            acceptedFlag=$flag.val() || '';
            // A new flag should not inherit an incompatible Critical/Warning/Info filter.
            acceptedSeverity='';$severity.val('');
            if(acceptedFlag) activateTab(tabForFlag(defsByCode.get(acceptedFlag.toLowerCase())));
            revealSelectedFlag=!!acceptedFlag;
            table.draw();
        });
        $severity.on('change.ffGrid',()=>{
            if(saving || !finishCurrentEdit()) {$severity.val(acceptedSeverity);return;}
            acceptedSeverity=$severity.val() || '';
            const definition=defsByCode.get(text($flag.val()).trim().toLowerCase());
            if(definition && severityOrder.includes(acceptedSeverity) && text(definition.Severity).trim().toLowerCase()!==acceptedSeverity) {
                acceptedFlag='';$flag.val('').trigger('change.select2');
            }
            table.draw();
        });
        $root.find('.ff-legend-filter').on('click.ffGrid',function(){const value=$(this).data('severity');$severity.val($severity.val()===value?'':value).trigger('change');});

        $(window).off('resize.ffGrid').on('resize.ffGrid',()=>{clearTimeout(resizeTimer);resizeTimer=setTimeout(()=>{if(!disposed) table.columns.adjust();},100);});
        $modal.on('shown.bs.modal.ffGrid',()=>table.columns.adjust());
        $root.find('#ffRetryPage').on('click.ffGrid',()=>{if(!saving) table.ajax.reload(null,false);});
        $root.find('#ffRetryCounts').on('click.ffGrid',loadFlagCounts);
        $root.find('#ffResetDetails').on('click.ffGrid',()=>updateFlagDetails());
        updateChangeUi();

        function validateStagedDates() {
            for(const row of rows) for(const [start,end] of datePairs) {
                if(!changedCells.has(selectedKey(row,start)) && !changedCells.has(selectedKey(row,end))) continue;
                if(row[start] && row[end] && row[end]<=row[start])
                    return `${end} must be later than ${start}.`;
            }
            return null;
        }
        function setSaving(value) {
            saving=value;
            $flag.add($severity).add($pageSize).prop('disabled',value);
            $root.find('.ff-legend-filter, #ffRetryPage, #ffRetryCounts, [role="tab"], [data-bs-dismiss="modal"]').prop('disabled',value);
            $root.find('.dataTables_paginate, .dt-paging').css('pointer-events',value?'none':'');
            rows.forEach((row,index)=>{
                columnFields.forEach(field=>{if(field) refreshCell(index,field);});
            });
            $button.html(value?'<i class="bx bx-loader-alt bx-spin me-1"></i> Saving...':'<i class="bx bx-sync"></i> Save all');
            updateChangeUi();
        }
        $grid.on('preDraw.dt.ffGrid',()=>!saving && !$grid.find('[aria-invalid="true"]:visible').length);
        $button.on('click.ffGrid',async function(){
            if(saving || !finishCurrentEdit()) return;
            $grid.find('tbody td.ff-text-edit:focus').each(function(){commitText(this);});
            const invalid=$grid.find('tbody .ff-date-input:not(:disabled)').toArray().find(input=>!input.validity.valid);
            if(invalid){invalid.focus();notify('error','Enter a complete valid date or clear the date field.');return;}
            const validation=validateStagedDates(); if(validation){notify('error',validation);return;}
            if(!changedCells.size) return;
            const edits=buildEdits();if(edits.length>1000){notify('warning','Save at most 1000 edited records at a time.');return;}
            if(activeRequest) {notify('warning','Wait for the current page to finish loading, then save.');return;}
            setSaving(true);
            let succeeded=false;
            try {
                const response=await fetch(config.saveUrl,{
                    method:'POST', credentials:'same-origin',
                    headers:{'Content-Type':'application/json','RequestVerificationToken':token},
                    body:JSON.stringify({FilingYear:config.filingYear,Edits:edits})});
                const result=await response.json().catch(()=>null);
                if(!response.ok || result?.success!==true || Number(result.failed||0)>0)
                    throw new Error(result?.message || `Save was not confirmed (HTTP ${response.status}). Your edits are retained; verify before retrying.`);
                changedCells.clear(); baseline.clear(); definitionsLoaded=false;succeeded=true;
                $root.find('#ffSaveStatus').prop('hidden',!result.flagRefreshRequired).text(result.flagRefreshRequired?'Changes saved. Flags and counts are awaiting validation.':'');
                countsRequest?.abort();countsRequest=null;++countsSequence;countsStarted=false;countsLoaded=false;
                notify('success',`Saved ${result.saved} employees (${result.savedCells} database cells).`);
                (result.warnings||[]).forEach(message=>notify('warning',message));
                if(Number(result.auditFailures)>0) notify('warning',`${result.auditFailures} audit writes failed. Check the application log.`);
                Promise.resolve().then(() => {
                    if (typeof window.reloadMainGrid === 'function') return window.reloadMainGrid();
                    if (typeof window.GridManager?.reload === 'function') return window.GridManager.reload();
                }).catch(() => notify('warning', 'Saved. Refresh the main employee list to see updated data.'));
            } catch(error) {notify('error',error.message || 'Save was not confirmed. Your edits remain available.');}
            finally {
                if(!disposed) {
                    setSaving(false);
                    // Failed saves must never replace the UI/baseline with fetched database values.
                    if (succeeded) window.bootstrap.Modal.getOrCreateInstance($modal[0]).hide();
                }
            }
        });
        $modal.on('hide.bs.modal.ffGrid',function(event){
            if(saving){event.preventDefault();return;}
            if(changedCells.size && !window.confirm('Discard your unsaved flag fixes?')) event.preventDefault();
        });
        const api={hasChanges:()=>changedCells.size>0,isSaving:()=>saving};
        root._flagGrid=api;
        window.disposeFlaggedGrid=function(){
            if(disposed) return;
            disposed=true;clearTimeout(resizeTimer);requestSequence++;countsSequence++;countsRequest?.abort();
            if(activeRequest) activeRequest.abort();
            $grid.off('.ffGrid');$button.off('.ffGrid');$flag.add($severity).add($pageSize).off('.ffGrid');$modal.off('.ffGrid');
            if($flag.hasClass('select2-hidden-accessible')) $flag.select2('destroy');
            $root.find('.ff-legend-filter,#ffRetryPage,#ffRetryCounts,#ffResetDetails,[role="tab"]').off('.ffGrid');$(window).off('resize.ffGrid');
            table.destroy();delete root._flagGrid;
        };
        return api;
    };
