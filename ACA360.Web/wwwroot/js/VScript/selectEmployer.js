
let lastInputValue = '';
let lastCursorPos = 0;
let wasInputFocused = false;
$(function () {
        let employerModalLoaded = false;
        //var employerName = localStorage.getItem('selectedEmployerName');
        //if (employerName) {
        //   /* $('.selectemployer').text(employerName);*/
        //    $('.selectemployer').text(employerName).attr('title', employerName);

        //} else {
        //    $('.selectemployer').text('Select Employer'); // fallback/default text
        //}
        $('#selectEmployerModal').on('show.bs.modal', function () {
            const $body = $('#selectEmployerModalBody');
           
        if (!employerModalLoaded) {
            $body.html('<div class="text-center p-4"><div class="spinner-border text-primary"></div></div>');

            $.get('/SelectEmployer/Index', function (html) {
            $body.html(html);
            setTimeout(function () {
                $('.Select_employerListContainer .searchRowContainer').addClass('mt-3');
                $('#selectEmployerModalBody .card-header').hide();
               
            }, 50);
            $('#selectEmployerModal').one('shown.bs.modal', function () {
                tryFocusSearchInput(); 
            });
        employerModalLoaded = true;
                }).fail(function () {
            $body.html('<div class="alert alert-danger m-3">Failed to load employer list.</div>');
                });
            }
        else {
            // Even if already loaded, ensure header is gone
            $body.find('.card-header:has(h4)').hide();
        }
        });
   
    

        if (document.getElementById("isEmployerIndexPage")) {
            // Disable modal trigger link
            $('.selectemployer').closest('a').addClass('disabled').removeAttr('data-bs-toggle data-bs-target').css({
                'pointer-events': 'none',
                'opacity': '0.6',
                'cursor': 'not-allowed'
            }).attr('title', 'You are already in Select Employer page');
            // 2. BACK BUTTON LOCK: Push a dummy history state immediately on page load
            window.history.pushState(null, "", window.location.href);
            window.onpopstate = function () {
                history.go(1); // Force the browser forward 1 step in history instantly
            };
        }

    

    $(document).on("click", ".page-link", function (e) {
        e.preventDefault();
        const pageIndex = $(this).data("pageindex");
        loadEmployerList(pageIndex);
    });

    $(document).on("change", ".drpPageSize", function () {
        loadEmployerList(1);
    });

    $(document).on('click', '.sortable', function () {
        let $this = $(this);
        if ($this.hasClass('active') && $this.hasClass('asc')) {
            $this.removeClass('asc').addClass('desc');
        } else if ($this.hasClass('active') && $this.hasClass('desc')) {
            $this.removeClass('desc').addClass('asc');
        } else {
            $('.sortable').removeClass('asc desc active');
            $this.addClass('asc active');
        }
        loadEmployerList(1);
    });

    $(document).on('submit', '#searchForm', function (e) {
        e.preventDefault();
        loadEmployerList(1);
    });

    $(document).on('change', '.typeFilter', function () {
        $('#searchForm').submit();
    });

    $(document).on('click', '.btnRefresh', function () {
        $('.searchInput').val('');
        $('.typeFilter').val('');
        $('.drpPageSize').val('10');
        loadEmployerList(1);
    });

    //$(document).on('click', '.select-employer-btn', function () {
    //    var employerId = $(this).data('id');
    //    var filingYear = $(this).data('filingyear');
    //    var employerName = $(this).data('name');
    //    await setSession("SelectedEmployerID", employerId);
    //    await setSession("SelectedFilingYear", filingYear);
    //    /*setCookie('selectedEmployer', employerId, 365);*/
    //    /*setCookie('fillingYear', filingYear, 365);*/
    //    localStorage.setItem('selectedEmployerName', employerName);
    //    window.location.href = '/Home/Index';
    //});
    $(document).on('keyup', '.searchInput', function () {
        const val = $(this).val();
        if (val.length >= 3 || val.length === 0) {
            clearTimeout(window.searchDebounceTimer);
            window.searchDebounceTimer = setTimeout(function () {
                loadEmployerList(1);
            }, 300); // debounce to avoid too many requests
        }
    });

    document.addEventListener('keydown', function (e) {
        if (e.ctrlKey && e.key === 'm') {
            //var modal = new bootstrap.Modal(document.getElementById('selectEmployerModal'));
            //modal.show();
            e.preventDefault();
            openSelectEmployerModal();
        }
    }); 
    // Track live cursor position
    $(document).on('keyup click', '.searchInput', function () {
        lastInputValue = this.value;
        lastCursorPos = this.selectionStart;
        wasInputFocused = document.activeElement === this;
    });  
    

});
function openSelectEmployerModal() {
   
    const isOnEmployerPage = document.getElementById("isEmployerIndexPage");

    if (isOnEmployerPage) {
        return;
    }
   
    const modalEl = document.getElementById('selectEmployerModal');
    bootstrap.Modal.getOrCreateInstance(modalEl).show();
    
    loadEmployerList(1); // Or loadEmployersIntoModal() if that's your custom modal loader
    // Remove any header regardless of text
      
    $('#selectEmployerModalBody .card-header').hide(); // or `.remove()` to delete

    $('.Select_employerListContainer .searchRowContainer').addClass('mt-3');
  
}
async function getEmployerRequestData(pageIndex) {
    let $activeSort = $('.sortable.active');
    let sortOrder = 'asc';
    if ($activeSort.length > 0) {
        sortOrder = $activeSort.hasClass('desc') ? 'desc' : 'asc';
    }
    let selectedFilingYear = await getSession('SelectedFilingYear') || new Date().getFullYear().toString();
    const requestData = {
        Search: $('.searchInput').val() || "",
        TypeFilter: $('.typeFilter').val() || "",
        // Parse these as integers. Use a fallback (like 1 or 10) if they are null/empty.
        PageIndex: parseInt(pageIndex) || 1,
        PageSize: parseInt($('.drpPageSize').val()) || 10,
        // FIX: Ensure SortColumn is an integer. If no active sort found, default to 0.
        SortColumn: parseInt($activeSort.data('sort')) || 0,
        SortOrder: sortOrder,
        // FIX: Match the C# model property name 'fillingYear' (two 'l's, lowercase 'f')
        fillingYear: selectedFilingYear,
        // Optional: Add the anti-forgery token directly to the data
        __RequestVerificationToken: $('input[name="__RequestVerificationToken"]').val()
    };
    return requestData;
}

async function loadEmployerList(pageIndex) {
    $("#loader").show();

    const requestData = await getEmployerRequestData(pageIndex); // ✅ Wait for selectedFilingYear

    $.ajax({
        url: '/SelectEmployer/SearchEmployers',
        type: "POST",
        data: requestData,
        success: function (data) {
            $('.Select_employerListContainer').html(data);
            $("#loader").hide();

            if ($('#selectEmployerModal').hasClass('show')) {
                $('.Select_employerListContainer .card-header').hide();
                $('.Select_employerListContainer .searchRowContainer').addClass('mt-3');
            }

            const $newInput = $('.Select_employerListContainer .searchInput');
            if ($newInput.length > 0) {
                $newInput.val(lastInputValue);

                if (wasInputFocused) {
                    setTimeout(() => {
                        $newInput.focus();
                        $newInput[0].setSelectionRange(lastCursorPos, lastCursorPos);
                    }, 10);
                }
            }
        },
        error: function () {
            $("#loader").hide();
        }
    });
}




function tryFocusSearchInput(attempts = 10) {
    const $searchInput = $('.Select_employerListContainer .searchInput');

    if ($searchInput.length > 0) {
      
        $searchInput.focus(); // focus and auto-select text
    } else if (attempts > 0) {
        setTimeout(() => tryFocusSearchInput(attempts - 1), 50); // retry until input is ready
    }
}


