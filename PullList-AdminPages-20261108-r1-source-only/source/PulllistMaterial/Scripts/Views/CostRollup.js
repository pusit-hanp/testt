(function (root, factory) {
    "use strict";
    var api = factory();
    if (typeof module === "object" && module.exports) module.exports = api;
    else {
        root.CostRollupClient = api;
        root.jQuery(function () { api.mount(root.jQuery, root.document, root); });
    }
}(typeof window !== "undefined" ? window : this, function () {
    "use strict";
    var fields = ["CUST_CODE", "COSTROLLUP_STATUS", "MISLIP_NO_REF", "CHANGE_BY", "FROM_DATE", "TO_DATE", "LAST_UPDATE"];

    function dateForServer(value) {
        if (!value) return "";
        var m = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2}))?$/.exec(value);
        if (!m) throw new Error("วันที่ไม่ถูกต้อง กรุณาระบุวันและเวลาโดยไม่ใส่ timezone");
        var year = +m[1], month = +m[2], day = +m[3];
        var leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
        var days = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
        if (year < 1 || month < 1 || month > 12 || day < 1 || day > days[month - 1] || +m[4] > 23 || +m[5] > 59 || +(m[6] || 0) > 59)
            throw new Error("วันที่หรือเวลาไม่ถูกต้อง");
        // Never construct JS Date: Oracle DATE has no timezone to convert.
        return value.length === 16 ? value + ":00" : value;
    }

    function originalPayload(original) {
        var payload = {};
        fields.forEach(function (field) { payload["Original." + field] = original[field] == null ? "" : original[field]; });
        return payload;
    }

    function appendCell(document, row, value) {
        var cell = document.createElement("td");
        cell.textContent = value == null ? "" : String(value);
        row.appendChild(cell);
        return cell;
    }

    function confirmHold(window, data) {
        if (data.COSTROLLUP_STATUS !== "Y") return true;
        var period = data.FROM_DATE ? "ช่วง " + data.FROM_DATE.replace("T", " ") + " ถึง " + data.TO_DATE.replace("T", " ") + " (เวลาของ Oracle server)" : "hold ไม่มีกำหนด";
        return window.confirm("ยืนยันตั้ง Cost Rollup เป็น Y ของ Customer " + data.CUST_CODE + "?\n" + period + "\nการเปลี่ยนแปลงมีผลเมื่อ service เริ่มรอบถัดไป");
    }

    function mount($, document, window) {
        var page = $("#costRollupPage");
        if (!page.length) return;
        var original = null, saving = false, searchVersion = 0, pendingSearch = null;
        var editor = $("#crEditor"), messageBox = $("#crMessage");

        function message(text, error) {
            messageBox.text(text).toggleClass("cr-error", !!error).prop("hidden", !text);
        }

        function closeEditor() { original = null; editor.prop("hidden", true); }

        function busy(value) {
            saving = value;
            $("#crSearchFields, #crEditFields").prop("disabled", value);
            $("#crRows button").prop("disabled", value);
        }

        function edit(row) {
            if (saving) return;
            original = row ? $.extend({}, row) : null;
            $("#crEditorTitle").text(row ? "แก้ไข Customer " + row.CUST_CODE : "เพิ่ม Customer");
            $("#crCustomer").val(row ? row.CUST_CODE : "").prop("readOnly", !!row);
            $("#crStatus").val(row ? row.COSTROLLUP_STATUS : "Y");
            $("#crStatus option[value='N']").prop("disabled", !row);
            $("#crFrom").val(row && row.FROM_DATE ? row.FROM_DATE : "");
            $("#crTo").val(row && row.TO_DATE ? row.TO_DATE : "");
            $("#crDelete").prop("hidden", !row);
            editor.prop("hidden", false);
            $(row ? "#crStatus" : "#crCustomer").trigger("focus");
        }

        function render(rows) {
            var body = document.getElementById("crRows");
            while (body.firstChild) body.removeChild(body.firstChild);
            rows.forEach(function (row) {
                var tr = document.createElement("tr");
                fields.forEach(function (field) {
                    var value = row[field];
                    var date = field === "FROM_DATE" || field === "TO_DATE" || field === "LAST_UPDATE";
                    var cell = appendCell(document, tr, date && value ? value.replace("T", " ") : value);
                    if (date) cell.className = "cr-date";
                });
                var action = appendCell(document, tr, "");
                var button = document.createElement("button");
                button.type = "button"; button.className = "btn btn-outline-primary btn-sm";
                button.textContent = "แก้ไข / ลบ"; button.disabled = saving;
                $(button).on("click", function () { edit(row); });
                action.appendChild(button); body.appendChild(tr);
            });
            $("#crCount").text(rows.length ? "พบ " + rows.length + " รายการ" : "ไม่พบรายการตามเงื่อนไข");
        }

        function errorText(xhr) {
            if (xhr.status === 401) return "Session หมดอายุ กรุณาเข้าสู่ระบบใหม่";
            if (xhr.status === 403) return "ไม่มีสิทธิ์ใช้หน้านี้";
            return xhr.responseJSON && xhr.responseJSON.message ? xhr.responseJSON.message : "ทำรายการไม่สำเร็จ กรุณาโหลดข้อมูลใหม่เพื่อตรวจสอบก่อนลองอีกครั้ง";
        }

        function search(keepMessage) {
            var version = ++searchVersion;
            if (pendingSearch) pendingSearch.abort();
            if (!keepMessage) message("", false);
            $("#crCount").text("กำลังโหลดข้อมูล");
            pendingSearch = $.ajax({ url: page.attr("data-search-url"), method: "GET", cache: false,
                data: { customer: $("#crSearchCustomer").val(), status: $("#crSearchStatus").val() }
            }).done(function (response) {
                if (version !== searchVersion) return;
                if (!response.status || !Array.isArray(response.rows)) { message(response.message || "โหลดข้อมูลไม่สำเร็จ", true); render([]); return; }
                render(response.rows);
            }).fail(function (xhr, reason) {
                if (version !== searchVersion || reason === "abort") return;
                render([]); message(errorText(xhr), true); $("#crCount").text("โหลดข้อมูลไม่สำเร็จ");
            }).always(function () { if (version === searchVersion) pendingSearch = null; });
        }

        function mutate(action, data) {
            if (saving) return;
            ++searchVersion;
            if (pendingSearch) { pendingSearch.abort(); pendingSearch = null; }
            data.__RequestVerificationToken = editor.find("input[name='__RequestVerificationToken']").val();
            busy(true);
            $.ajax({ url: page.attr("data-" + action + "-url"), method: "POST", data: data })
                .done(function (response) {
                    if (!response.status) { message(response.message || "ทำรายการไม่สำเร็จ", true); return; }
                    closeEditor(); message(response.message, false); search(true);
                })
                .fail(function (xhr) {
                    message(errorText(xhr), true);
                    var outcome = xhr.responseJSON && xhr.responseJSON.outcome;
                    if (outcome !== "VALIDATION") { closeEditor(); search(true); }
                })
                .always(function () { busy(false); });
        }

        $("#crSearchForm").on("submit", function (event) { event.preventDefault(); if (!saving) { closeEditor(); search(false); } });
        $("#crAdd").on("click", function () { edit(null); });
        $("#crCancel").on("click", function () { if (!saving) closeEditor(); });
        editor.on("submit", function (event) {
            event.preventDefault();
            if (saving) return;
            try {
                var data = original ? originalPayload(original) : {};
                data.CUST_CODE = $.trim($("#crCustomer").val()).toUpperCase();
                data.COSTROLLUP_STATUS = original ? $("#crStatus").val() : "Y";
                data.FROM_DATE = dateForServer($("#crFrom").val()); data.TO_DATE = dateForServer($("#crTo").val());
                if (!/^[A-Z0-9]{2}$/.test(data.CUST_CODE)) throw new Error("Customer ต้องเป็นตัวอักษรหรือตัวเลข 2 ตัว เช่น 0C");
                if (!!data.FROM_DATE !== !!data.TO_DATE) throw new Error("กรอก From และ To ให้ครบ หรือเว้นว่างทั้งคู่");
                if (data.FROM_DATE && data.FROM_DATE > data.TO_DATE) throw new Error("From ต้องไม่เกิน To");
                if (!confirmHold(window, data)) return;
                mutate(original ? "update" : "add", data);
            } catch (error) { message(error.message, true); }
        });
        $("#crDelete").on("click", function () {
            if (saving || !original) return;
            if (window.confirm("ลบ Cost Rollup ของ Customer " + original.CUST_CODE + "? การเปลี่ยนแปลงมีผลใน service รอบถัดไป"))
                mutate("delete", originalPayload(original));
        });
        search(false);
    }

    return { dateForServer: dateForServer, originalPayload: originalPayload, appendCell: appendCell, confirmHold: confirmHold, mount: mount };
}));
