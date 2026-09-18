$(document).ready(function () {


    //Add function

    $("#add_new").click(function () {
        var add_url = '/Plan/Add_New';
        $.ajax({
            type: "POST",
            url: add_url,
            success: function (data) {
                $(".modal-body").html(data);
                $("#launch_modal").click();
            },
            error: function (xhr) {
                //console.log("Error response:", xhr.responseText);
                alert("Save failed.");
            }
        });
    });

});