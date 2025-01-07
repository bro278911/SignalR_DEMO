// 添加調試信息
function addDebugMessage(message) {
    const debugDiv = document.getElementById("debugInfo");
    if (debugDiv) {
        const messageDiv = document.createElement("div");
        messageDiv.textContent = `${new Date().toLocaleTimeString()}: ${message}`;
        debugDiv.insertBefore(messageDiv, debugDiv.firstChild);

        // 保持最新的 100 條消息
        while (debugDiv.children.length > 100) {
            debugDiv.removeChild(debugDiv.lastChild);
        }
    }
}

// 格式化日期時間
function formatDateTime(dateString) {
    const date = new Date(dateString);
    return date.toLocaleString('zh-TW', {
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit'
    });
}

// 高亮顯示行
function highlightRow(row) {
    row.style.backgroundColor = '#ffd700';
    setTimeout(() => {
        row.style.backgroundColor = '';
    }, 2000);
}
