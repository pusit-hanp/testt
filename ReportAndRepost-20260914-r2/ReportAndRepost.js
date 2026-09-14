(function () {
    "use strict";
    var table;
    var busy = false;
    var searched = false;
    var criteria;
    var latestDraw = 0;
    var loading = false;
    var blockedRows = {}; // Only uncertain results on this open page; never automatic retries.

    function getCriteria() {
        var option = $("#status_op option:selected");
        return {
            CTN: $("#s_ctn").val(), PN: $("#s_part").val(), BATCH: $("#s_batch").val(),
            MI_SLIPNUMBER: $("#s_slip").val(), DATE_FROM: $("#s_issuedtfm").val(), DATE_TO: $("#s_issuedtto").val(),
            FROM_LOCATION: $("#s_locfm").val(), TO_LOCATION: $("#s_locto").val(),
            STATUS_MODE: option.attr("data-mode") || option.val(), STATUS_VALUE: option.attr("data-status") || ""
        };
    }

    function selectedRows() {
        var rows = [];
        table.rows({ page: "current" }).every(function () {
            var row = this.data();
            if (row.CAN_REPOST && !blockedRows[row.ROW_ID] && $(this.node()).find("input.row-select").prop("checked")) rows.push(row);
        });
        return rows;
    }

    function selectionChanged() {
        var count = selectedRows().length;
        $("#selectionCount").text("Selected: " + count);
        $("#btnRepost").prop("disabled", busy || loading || count === 0);
    }

    function statusOptions(statuses) {
        var current = getCriteria();
        $("#status_op optgroup").remove();
        var group = $("<optgroup>").attr("label", "Exact status in search results");
        $.each(statuses || [], function (index, item) {
            if (item.STATUS_TRANFER == null || $.trim(item.STATUS_TRANFER) === "") return;
            var option = $("<option>").val("exact-" + index).attr("data-mode", "EXACT")
                .attr("data-status", item.STATUS_TRANFER).text(item.STATUS_TRANFER + " (" + item.TOTAL + ")");
            group.append(option);
        });
        // Keep the selected exact status even when its last row has just become Success.
        if (current.STATUS_MODE === "EXACT") {
            var found = false;
            group.find("option").each(function () {
                if ($(this).attr("data-status") === current.STATUS_VALUE) { $(this).prop("selected", true); found = true; }
            });
            if (!found) group.append($("<option>").val("exact-current").attr("data-mode", "EXACT")
                .attr("data-status", current.STATUS_VALUE).text(current.STATUS_VALUE + " (0)").prop("selected", true));
        }
        $("#status_op").append(group);
        if (current.STATUS_MODE !== "EXACT") $("#status_op").val(current.STATUS_MODE);
    }

    function loadLocations(id, type) {
        // Same lookup and user_name value as the uncommented CTN Report search.
        $.ajax({ url: ReportAndRepostUrls.locations, type: "GET", data: { user_name: "ADMIN", type: type } })
            .done(function (data) {
                if (!data.status) { $("#reportMessage").text(data.msg || "Location lookup failed."); return; }
                var rows;
                try { rows = typeof data.msg === "string" ? JSON.parse(data.msg) : data.msg; }
                catch (e) { $("#reportMessage").text("Invalid location response."); return; }
                $.each(rows || [], function (i, row) { $(id).append($("<option>").val(row.LOCATION).text(row.LOCATION)); });
            }).fail(function () { $("#reportMessage").text("Cannot load locations."); });
    }

    function setBusy(value) {
        busy = value;
        $("#searchFields").prop("disabled", value);
        $("#reportTableArea").toggleClass("is-busy", value);
        $("#dt_ReportRepost input.row-select").prop("disabled", value);
        selectionChanged();
    }

    function addResult(row, message) {
        $("#repostResults").append($("<tr>").append($("<td>").text(row.MI_SLIPNUMBER),
            $("<td>").text(row.CTN), $("<td>").text(message)));
    }

    function runBatch(rows, index) {
        if (index >= rows.length) {
            setBusy(false);
            $("#repostProgress").text("Finished " + rows.length + " item(s).");
            table.ajax.reload(null, true);
            return;
        }
        var row = rows[index];
        $("#repostProgress").text("Sending " + (index + 1) + " / " + rows.length);
        // One POST per row keeps a selected batch from exceeding the legacy MVC request timeout.
        $.ajax({
            url: ReportAndRepostUrls.repost, type: "POST", dataType: "json",
            data: { rowId: row.ROW_ID, __RequestVerificationToken: $("input[name='__RequestVerificationToken']").val() }
        }).done(function (result) {
            addResult(row, result.Message || "No result message.");
            if (result.Outcome === "SMART_SUCCESS_DB_FAILED" || result.Outcome === "SMART_RESULT_UNKNOWN") blockedRows[row.ROW_ID] = true;
            runBatch(rows, index + 1);
        }).fail(function (xhr) {
            // The server may have sent to SmartWH before the browser lost its response.
            blockedRows[row.ROW_ID] = true;
            var message = xhr.responseJSON && (xhr.responseJSON.Message || xhr.responseJSON.error);
            addResult(row, message || "Request result is unknown. Check before sending again.");
            for (var i = index + 1; i < rows.length; i++) addResult(rows[i], "Not sent; batch stopped.");
            setBusy(false);
            $("#repostProgress").text("Stopped. Check the failed request before retrying.");
            table.ajax.reload(null, true);
        });
    }

    $(document).ready(function () {
        var today = new Date();
        var from = new Date(today.getFullYear(), today.getMonth(), today.getDate() - 6);
        $("#s_issuedtfm, #s_issuedtto").datepicker({ format: "mm/dd/yyyy", todayHighlight: true, autoclose: true });
        $("#s_issuedtfm").datepicker("setDate", from);
        $("#s_issuedtto").datepicker("setDate", today);
        loadLocations("#s_locfm", "from_lo");
        loadLocations("#s_locto", "to_stro");
        var textRenderer = $.fn.dataTable.render.text();
        table = $("#dt_ReportRepost").DataTable({
            serverSide: true, processing: true, searching: false, ordering: false,
            pageLength: 25, lengthChange: false,
            preDrawCallback: function () { return !busy; },
            ajax: function (data, callback) {
                latestDraw = data.draw;
                $("#selectionCount").text("Selected: 0"); $("#btnRepost").prop("disabled", true);
                if (!searched) { callback({ draw: data.draw, recordsTotal: 0, recordsFiltered: 0, data: [] }); return; }
                loading = true;
                $.ajax({ url: ReportAndRepostUrls.search, type: "GET", dataType: "json",
                    data: $.extend({}, criteria, { draw: data.draw, start: data.start, length: data.length })
                }).done(function (result) {
                    // Ignore stale requests after a newer search/page request.
                    if (data.draw !== latestDraw) return;
                    loading = false;
                    statusOptions(result.statuses);
                    callback(result);
                }).fail(function (xhr) {
                    if (data.draw !== latestDraw) return;
                    loading = false;
                    $("#reportMessage").text(xhr.responseJSON && xhr.responseJSON.error || "Search failed.");
                    callback({ draw: data.draw, recordsTotal: 0, recordsFiltered: 0, data: [] });
                });
            },
            columns: [
                { data: null, className: "select-cell", render: function (data, type, row) {
                    if (type !== "display") return "";
                    if (row.CAN_REPOST && !blockedRows[row.ROW_ID]) return '<input type="checkbox" class="row-select" aria-label="Select CTN" />';
                    var reason = blockedRows[row.ROW_ID] ? "Check the previous result before re-post." : row.BLOCK_REASON || "Cannot re-post.";
                    return '<span class="text-muted">' + textRenderer.display(reason) + '</span>';
                } },
                { data: "MI_SLIPNUMBER", render: textRenderer },
                { data: "FROM_LOCATION", render: textRenderer }, { data: "TO_LOCATION", render: textRenderer },
                { data: "CTN", render: textRenderer },
                { data: "RFID_CART", defaultContent: "", render: textRenderer },
                { data: "RFID_TAG", defaultContent: "", render: textRenderer },
                { data: "PN", render: textRenderer }, { data: "BATCH", render: textRenderer }, { data: "QTY", render: textRenderer },
                { data: "REASON", className: "reason-text", defaultContent: "", render: textRenderer },
                { data: "ISSUE_BY", defaultContent: "", render: textRenderer },
                { data: "APPROVE_DATE", render: function (data) { return data ? moment(data).format("YYYY-MM-DD HH:mm:ss") : ""; } },
                { data: "ACT_BIN", defaultContent: "", render: textRenderer },
                { data: "SAP_DOC1", defaultContent: "", render: textRenderer },
                { data: "STATUS_TRANFER", className: "status-text", defaultContent: "(NULL)", render: textRenderer }
            ],
            drawCallback: function () {
                $("#dt_ReportRepost input.row-select").prop("checked", false);
                $("#selectionCount").text("Selected: 0"); $("#btnRepost").prop("disabled", true);
            }
        });
        $("#dt_ReportRepost").on("change", "input.row-select", selectionChanged);
        $("#formReportRepost").on("submit", function (e) {
            e.preventDefault(); if (busy) return;
            criteria = getCriteria(); searched = true;
            $("#reportMessage").text(""); table.ajax.reload(null, true);
        });
        $("#status_op").on("change", function () { if (searched && !busy) $("#formReportRepost").trigger("submit"); });
        $("#btnReset").on("click", function () {
            if (busy) return;
            $("#s_ctn, #s_part, #s_batch, #s_slip").val("");
            $("#s_locfm, #s_locto").val("-1"); $("#status_op").val("FAIL");
            $("#formReportRepost").trigger("submit");
        });
        $("#btnRepost").on("click", function () {
            if (busy || loading) return;
            var rows = selectedRows(); if (!rows.length) return;
            setBusy(true);
            Swal.fire({ title: "Re-post " + rows.length + " CTN(s)?", icon: "question", showCancelButton: true,
                text: rows.map(function (row) { return row.MI_SLIPNUMBER + " / " + row.CTN; }).join("\n")
            }).then(function (answer) {
                if (!answer.isConfirmed) { setBusy(false); return; }
                $("#repostResults").empty(); $("#repostResultsArea").show();
                runBatch(rows, 0);
            });
        });
    });
}());
