using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using System.Data;

namespace signalR_test.Hubs
{
    public class ChatHub : Hub
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;
        private SqlDependency _dependency;
        private SqlConnection _connection;
        private readonly ILogger<ChatHub> _logger;
        private bool _isMonitoring = false;

        public ChatHub(IConfiguration configuration, ILogger<ChatHub> logger)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
            _logger = logger;
        }

        public async Task SendDatabaseUpdate()
        {
            try
            {
                _logger.LogInformation("開始獲取資料庫更新");
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    var query = @"
                        SELECT TOP 100 *
                        FROM [Cloud_EMS].[dbo].[ChargingStorage] WITH (NOLOCK)
                        ORDER BY ChargingID DESC, ReceivedTime DESC";

                    _logger.LogInformation($"執行查詢: {query}");

                    using (var command = new SqlCommand(query, connection))
                    {
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            var data = new List<Dictionary<string, object>>();
                            while (await reader.ReadAsync())
                            {
                                var row = new Dictionary<string, object>();
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    var value = reader.GetValue(i);
                                    row[reader.GetName(i)] = value == DBNull.Value ? null : value;
                                }
                                data.Add(row);
                            }
                            _logger.LogInformation($"找到 {data.Count} 筆資料");
                            await Clients.All.SendAsync("ReceiveDatabaseUpdate", data);
                            _logger.LogInformation("資料已發送到客戶端");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SendDatabaseUpdate 發生錯誤");
                await Clients.Caller.SendAsync("ReceiveError", ex.Message);
            }
        }

        public async Task StartDatabaseMonitoring()
        {
            if (_isMonitoring)
            {
                _logger.LogInformation("已經在監控中，跳過啟動");
                return;
            }

            try
            {
                _logger.LogInformation("開始資料庫監控");
                StopMonitoring();

                _connection = new SqlConnection(_connectionString);
                await _connection.OpenAsync();
                _logger.LogInformation("資料庫連接已開啟");

                // 確保 Service Broker 已啟用
                using (var command = new SqlCommand("SELECT is_broker_enabled FROM sys.databases WHERE name = 'Cloud_EMS'", _connection))
                {
                    var isBrokerEnabled = (bool)await command.ExecuteScalarAsync();
                    if (!isBrokerEnabled)
                    {
                        _logger.LogError("Service Broker 未啟用！");
                        throw new Exception("Database Service Broker is not enabled!");
                    }
                }

                SqlDependency.Start(_connectionString);
                _logger.LogInformation("SQL Dependency 已啟動");

                await SetupDependency();
                _isMonitoring = true;

                await Clients.Caller.SendAsync("ReceiveMessage", "資料庫監控已啟動");
                _logger.LogInformation("資料庫監控已成功啟動");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "啟動監控時發生錯誤");
                await Clients.Caller.SendAsync("ReceiveError", $"啟動監控錯誤: {ex.Message}");
                _isMonitoring = false;
            }
        }

        private async Task SetupDependency()
        {
            try
            {
                _logger.LogInformation("設置新的依賴");
                var query = @"
                    SELECT TOP 100 *
                    FROM [Cloud_EMS].[dbo].[ChargingStorage] WITH (NOLOCK)
                    ORDER BY ChargingID DESC, ReceivedTime DESC";

                using (var command = new SqlCommand(query, _connection))
                {
                    command.CommandTimeout = 60;
                    command.Notification = null;

                    _logger.LogInformation("創建 SQL Dependency");
                    _dependency = new SqlDependency(command);
                    _dependency.OnChange += DependencyOnChange;

                    _logger.LogInformation("執行初始查詢");
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        var data = new List<Dictionary<string, object>>();
                        while (await reader.ReadAsync())
                        {
                            var row = new Dictionary<string, object>();
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                var value = reader.GetValue(i);
                                row[reader.GetName(i)] = value == DBNull.Value ? null : value;
                            }
                            data.Add(row);
                        }
                        _logger.LogInformation($"初始資料載入: {data.Count} 筆");
                        await Clients.All.SendAsync("ReceiveDatabaseUpdate", data);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SetupDependency 發生錯誤");
                await Clients.Caller.SendAsync("ReceiveError", $"設置依賴錯誤: {ex.Message}");
            }
        }

        private void DependencyOnChange(object sender, SqlNotificationEventArgs e)
        {
            _logger.LogInformation($"收到資料庫變更通知: Type={e.Type}, Info={e.Info}, Source={e.Source}");

            if (e.Type == SqlNotificationType.Change)
            {
                _ = SendDatabaseUpdate();
                _ = SetupDependency();
            }
            else
            {
                _logger.LogWarning($"收到非變更通知: {e.Type}");
            }
        }

        private void StopMonitoring()
        {
            try
            {
                _logger.LogInformation("停止監控");
                if (_dependency != null)
                {
                    _dependency.OnChange -= DependencyOnChange;
                    _dependency = null;
                }

                if (_connection != null)
                {
                    if (_connection.State == ConnectionState.Open)
                    {
                        _connection.Close();
                    }
                    _connection.Dispose();
                    _connection = null;
                }

                try
                {
                    SqlDependency.Stop(_connectionString);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "停止 SQL Dependency 時發生錯誤");
                }

                _isMonitoring = false;
                _logger.LogInformation("監控已停止");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StopMonitoring 發生錯誤");
            }
        }

        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation($"客戶端連接: {Context.ConnectionId}");
            await SendDatabaseUpdate();
            await Clients.All.SendAsync("ReceiveMessage", "新用戶已連接!");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            _logger.LogInformation($"客戶端斷開連接: {Context.ConnectionId}");
            StopMonitoring();
            await Clients.All.SendAsync("ReceiveMessage", "用戶已斷開連接!");
            await base.OnDisconnectedAsync(exception);
        }

        public async Task SendMessage(string message)
        {
            await Clients.All.SendAsync("ReceiveMessage", message);
        }
    }
}