"use strict";

document.addEventListener("DOMContentLoaded", function () {
    var connection = new signalR.HubConnectionBuilder()
        .withUrl("/chatHub", {
            skipNegotiation: false,
            transport: signalR.HttpTransportType.WebSockets,
        })
        .configureLogging(signalR.LogLevel.Information)
        .build();

    var connectionStatusDiv = document.getElementById("connectionStatus");
    var messagesList = document.getElementById("messagesList");
    var sendButton = document.getElementById("sendButton");
    var messageInput = document.getElementById("messageInput");

    function updateConnectionStatus(status, isError = false) {
        connectionStatusDiv.className = `alert alert-${isError ? "danger" : "success"}`;
        connectionStatusDiv.textContent = status;
    }

    function addMessage(message, isError = false) {
        var messageDiv = document.createElement("div");
        messageDiv.textContent = `${new Date().toLocaleTimeString()}: ${message}`;
        messageDiv.className = `p-2 mb-2 rounded ${isError ? "bg-danger text-white" : "bg-light"}`;
        messagesList.appendChild(messageDiv);
        messagesList.scrollTop = messagesList.scrollHeight;
    }

    connection.on("ReceiveMessage", function (message) {
        addMessage(message);
        console.log("Received message: " + message);
    });

    connection.onreconnecting(function (error) {
        updateConnectionStatus("Attempting to reconnect...", true);
        sendButton.disabled = true;
    });

    connection.onreconnected(function (connectionId) {
        updateConnectionStatus("Connected to server");
        sendButton.disabled = false;
    });

    connection.onclose(function (error) {
        updateConnectionStatus(
            "Connection closed. Refresh page to reconnect.",
            true
        );
        sendButton.disabled = true;
    });

    function startConnection() {
        console.log("Starting connection...");
        connection
            .start()
            .then(function () {
                console.log("Connected successfully!");
                updateConnectionStatus("Connected to server");
                sendButton.disabled = false;
                addMessage("Connected to chat");
            })
            .catch(function (err) {
                console.error("Connection failed: " + err.toString());
                updateConnectionStatus(
                    "Connection failed: " + err.toString(),
                    true
                );
                setTimeout(startConnection, 5000);
            });
    }

    // Enter 鍵發送訊息
    messageInput.addEventListener("keypress", function (event) {
        if (event.key === "Enter") {
            event.preventDefault();
            sendButton.click();
        }
    });

    // 發送按鈕點擊事件
    sendButton.addEventListener("click", function (event) {
        var message = messageInput.value.trim();

        if (message) {
            connection.invoke("SendMessage", message).catch(function (err) {
                addMessage("Error sending message: " + err.toString(), true);
                console.error(err.toString());
            });

            messageInput.value = "";
        }

        event.preventDefault();
    });

    // 初始時禁用發送按鈕
    sendButton.disabled = true;

    // 開始連接
    startConnection();
});
