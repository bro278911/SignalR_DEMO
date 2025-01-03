"use strict";

document.addEventListener("DOMContentLoaded", function () {
    var connection = new signalR.HubConnectionBuilder()
        .withUrl("/chatHub")
        .withAutomaticReconnect()
        .build();

    connection.on("ReceiveMessage", function (message) {
        var li = document.createElement("li");
        li.textContent = message;
        document.getElementById("messagesList").appendChild(li);
    });

    connection.on("ReceiveDatabaseUpdate", function (data) {
        console.log("收到資料庫更新:", data);
        var table = document.getElementById("dataTable");
        if (!table) return;

        // 清空表格內容（保留表頭）
        while (table.rows.length > 1) {
            table.deleteRow(1);
        }

        // 添加新數據
        data.forEach(function (item) {
            var row = table.insertRow();
            Object.values(item).forEach(function (value) {
                var cell = row.insertCell();
                cell.textContent = value;
            });
        });
    });

    connection.start().catch(function (err) {
        return console.error(err.toString());
    });
});
