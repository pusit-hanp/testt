(function ($, Board) {
    "use strict";
    $(function () {
        var root = $("#serviceMonitoring");
        if (!root.length || !Board) return;
        var summarySequence = 0, board;
        var storage = null;
        try { storage = window.sessionStorage; } catch (ignored) {}

        function display(value) { return value === null || typeof value === "undefined" || value === "" ? "–" : String(value); }
        function time(value) {
            if (!value) return "ยังไม่ได้โหลด";
            var match = /^\/Date\((-?\d+)(?:[+-]\d+)?\)\/$/.exec(value);
            var date = new Date(match ? Number(match[1]) : value);
            return isNaN(date.getTime()) ? display(value) : date.toLocaleString();
        }
        function errorMessage(xhr) {
            if (xhr.status === 401 || xhr.status === 403) return "session หมดอายุหรือไม่มีสิทธิ์ กรุณาเข้าสู่ระบบและตรวจสอบสิทธิ์อีกครั้ง";
            return xhr.responseJSON && xhr.responseJSON.message ? xhr.responseJSON.message : "โหลดข้อมูลไม่สำเร็จ กรุณาลองใหม่";
        }
        function textNode(tag, value, className) {
            var node = $("<" + tag + ">").text(display(value));
            if (className) node.addClass(className);
            return node;
        }
        function summaryRows(rows) {
            var container = $("#monitoringSummary").empty();
            var table = $("<table>").addClass("sm-counts").attr("aria-label", "Service counts by prefix and movement");
            var firstHead = $("<tr>").append(textNode("th", "Prefix").attr({ rowspan: "2", scope: "col" }));
            var secondHead = $("<tr>");
            [311, 313, 315, "Total"].forEach(function (movement) {
                firstHead.append(textNode("th", movement).attr({ colspan: "2", scope: "colgroup" }).addClass(movement === "Total" ? "sm-total" : ""));
                var waitingStatus = movement === 311 ? 5 : movement === 313 ? 10 : 14;
                var postingStatus = movement === 311 ? 8 : movement === 313 ? 11 : 15;
                secondHead.append(textNode("th", "Waiting Service Pickup" + (movement === "Total" ? "" : " (" + waitingStatus + ")")).attr("scope", "col"))
                    .append(textNode("th", "Posting" + (movement === "Total" ? "" : " (" + postingStatus + ")")).attr("scope", "col"));
            });
            table.append($("<thead>").append(firstHead).append(secondHead));
            var body = $("<tbody>");
            ["GT-", "MU-", "PK-"].forEach(function (prefix) {
                var rowElement = $("<tr>").append(textNode("th", prefix).attr("scope", "row"));
                var waitingTotal = 0, postingTotal = 0;
                [311, 313, 315].forEach(function (movement) {
                    var found = null;
                    (rows || []).forEach(function (row) { if (row.Prefix === prefix && row.Movement === movement) found = row; });
                    var na = prefix === "PK-" && movement === 311;
                    if (found && !na) { waitingTotal += Number(found.Waiting) || 0; postingTotal += Number(found.Posting) || 0; }
                    rowElement.append(textNode("td", na ? "N/A" : found ? found.Waiting : "–"))
                        .append(textNode("td", na ? "N/A" : found ? found.Posting : "–"));
                });
                body.append(rowElement.append(textNode("td", rows ? waitingTotal : "–", "sm-total"))
                    .append(textNode("td", rows ? postingTotal : "–", "sm-total")));
            });
            container.append(table.append(body));
        }
        function refreshSummary() {
            var sequence = ++summarySequence;
            $("#monitoringSummaryRefresh").prop("disabled", true).text("กำลังโหลด...");
            $("#monitoringSummaryError").text("");
            $.ajax({ url: root.attr("data-summary-url"), type: "GET", dataType: "json", cache: false })
                .done(function (response) {
                    if (sequence !== summarySequence) return;
                    if (!response || response.ok !== true || !Array.isArray(response.data)) {
                        $("#monitoringSummaryError").text(response && response.message || "โหลดจำนวน slips ไม่สำเร็จ"); return;
                    }
                    summaryRows(response.data);
                    $("#monitoringSummaryTime").text("โหลดล่าสุด " + time(response.loadedAt));
                }).fail(function (xhr) {
                    if (sequence === summarySequence) $("#monitoringSummaryError").text(errorMessage(xhr));
                }).always(function () {
                    if (sequence === summarySequence) $("#monitoringSummaryRefresh").prop("disabled", false).text("Refresh counts");
                });
        }
        function metadata(fields) {
            var table = $("<table>").addClass("sm-header-table").attr("aria-label", "Slip header"), body = $("<tbody>");
            for (var i = 0; i < fields.length; i += 2) {
                var row = $("<tr>").append(textNode("th", fields[i][0]).attr("scope", "row")).append(textNode("td", fields[i][1]));
                if (fields[i + 1]) row.append(textNode("th", fields[i + 1][0]).attr("scope", "row")).append(textNode("td", fields[i + 1][1]));
                else row.append($("<td>").attr("colspan", "2"));
                body.append(row);
            }
            return table.append(body);
        }
        function renderBoard() {
            if (!board) return;
            var entries = board.entries(), container = $("#monitoringBoard").empty(), pending = 0;
            $("#monitoringBoardRefresh, #monitoringBoardClear").prop("disabled", entries.length === 0);
            entries.forEach(function (entry) {
                if (entry.loading) pending++;
                var group = $("<article>").addClass("sm-group").attr("data-slip-id", entry.id);
                var title = textNode("h3", entry.id);
                if (entry.data && entry.data.Found) title.append(textNode("span", entry.data.Source, "sm-source"));
                var remove = $("<button>").attr({ type: "button", "aria-label": "Remove " + entry.id }).addClass("btn btn-default btn-sm")
                    .text("Remove slip").on("click", function () { board.remove(entry.id); });
                group.append($("<div>").addClass("sm-group-header").append(title).append(remove));
                group.append(textNode("p", "โหลดล่าสุด " + time(entry.loadedAt) + (entry.loading ? " · กำลังโหลด..." : ""), "sm-muted"));
                if (entry.error) group.append(textNode("p", entry.error, "sm-error").attr("role", "alert"));
                var data = entry.data;
                if (data && !data.Found) group.append(textNode("p", "ไม่พบ MISLIP_NO นี้ใน TPHEADER และ WIPHEADER"));
                if (data && data.Found) {
                    group.append(metadata([
                        ["STATUS", data.Source === "WIP" ? "N/A (WIP)" : data.Status], ["POST_NO", data.Source === "WIP" ? "N/A (WIP)" : data.PostNo],
                        ["SAP_DOC", data.SapDoc], ["SAP_DOC1", data.SapDoc1], ["Plant", data.Plant],
                        ["From / To SLoc", display(data.FromSloc) + " / " + display(data.ToSloc)],
                        ["Assembly", data.Assembly], ["Planned order", data.PlannedOrder]
                    ]));
                    group.append($("<details>").append(textNode("summary", "Header details")).append(metadata([
                        ["Pull list no.", data.PullListNo], ["Planned quantity", data.PlannedQty],
                        ["Document date", data.DocDate], ["Post date", data.PostDate],
                        ["Issue date", data.IssueDate], ["Receive date", data.ReceiveDate],
                        ["Requested by", data.RequestBy], ["Issued by", data.IssueBy], ["Received by", data.ReceiveBy], ["Remark", data.Remark]
                    ])));
                    var items = Array.isArray(data.Items) ? data.Items : [];
                    if (!items.length) group.append(textNode("p", "พบ header แต่ไม่มี item ใน " + (data.Source === "TP" ? "TPITEMS" : "WIPITEMS")));
                    else {
                        var table = $("<table>").addClass("table table-bordered sm-items");
                        var head = $("<tr>");
                        ["Material", "Description", "UOM", "Pull qty", "Issue qty", "Item remark", "TCO status", "TCO batch"].forEach(function (label) { head.append(textNode("th", label).attr("scope", "col")); });
                        var body = $("<tbody>");
                        items.forEach(function (item) {
                            var row = $("<tr>");
                            [item.Material, item.Description, item.Uom, item.PullQty, item.IssueQty, item.ItemRemark,
                                data.Source === "WIP" ? "N/A" : item.TcoSts, data.Source === "WIP" ? "N/A" : item.TcoBatch]
                                .forEach(function (value, index) { row.append(textNode("td", value, index === 3 || index === 4 ? "sm-number" : "")); });
                            body.append(row);
                        });
                        group.append($("<div>").addClass("table-responsive").append(table.append($("<thead>").append(head)).append(body)));
                    }
                }
                container.append(group);
            });
            if (!entries.length) container.append(textNode("p", "ยังไม่มีรายการติดตาม", "sm-muted"));
            $("#monitoringBoardStatus").text(entries.length + " / 50 slips" + (pending ? " · กำลังโหลด " + pending + " slips" : ""));
        }
        board = Board.create({
            storage: storage,
            key: "PullList:ServiceMonitoring:v1:" + root.attr("data-app-path") + ":" + root.attr("data-user-key"),
            changed: renderBoard,
            notice: function (message, persistent) { $(persistent ? "#monitoringStorageNotice" : "#monitoringNotice").text(message); },
            request: function (ids, success, failure) {
                return $.ajax({ url: root.attr("data-batch-url"), type: "POST", dataType: "json", traditional: true,
                    data: { ids: ids, __RequestVerificationToken: $("#monitoringSearchForm input[name='__RequestVerificationToken']").val() } })
                    .done(success).fail(function (xhr) { failure(errorMessage(xhr)); });
            }
        });
        $("#monitoringSearchForm").on("submit", function (event) {
            event.preventDefault(); $("#monitoringNotice").text("");
            if (board.search($("#monitoringSlip").val())) $("#monitoringSlip").val("").focus();
        });
        $("#monitoringSummaryRefresh").on("click", refreshSummary);
        $("#monitoringBoardRefresh").on("click", function () { board.refresh(); });
        $("#monitoringBoardClear").on("click", function () { board.clear(); });
        summaryRows(null); renderBoard(); refreshSummary(); board.restore();
    });
}(jQuery, window.PullListMonitoringBoard));
