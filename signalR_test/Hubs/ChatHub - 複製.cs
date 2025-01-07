using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using System.Data;

namespace signalR_test.Hubs
{
    public class ChatHub2 : Hub
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;
        private readonly ILogger<ChatHub> _logger;
        private readonly IHubContext<ChatHub> _hubContext;
        private static SqlDependency _dependency;
        private static SqlConnection _connection;
        private static readonly object _lock = new object();
        private static int _connectedClients = 0;
        private static bool _isMonitoring = false;
        private static int _lastKnownChargingId = 0;

        public ChatHub2(IConfiguration configuration, ILogger<ChatHub> logger, IHubContext<ChatHub> hubContext)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
            _logger = logger;
            _hubContext = hubContext;
        }

        public override async Task OnConnectedAsync()
        {
            try
            {
                lock (_lock)
                {
                    _connectedClients++;
                    _logger.LogInformation($"客戶端連接成功: {Context.ConnectionId}, 當前連接數: {_connectedClients}");

                    // 只在第一個客戶端連接時啟動監控
                    if (_connectedClients == 1)
                    {
                        _ = StartDatabaseMonitoring();
                    }
                }

                // 立即發送當前數據給新連接的客戶端
                await SendDatabaseUpdate();
                await Clients.Caller.SendAsync("ReceiveMessage", "連接成功，開始接收實時更新");
                await base.OnConnectedAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"客戶端連接時發生錯誤: {Context.ConnectionId}");
                await Clients.Caller.SendAsync("ReceiveError", $"連接錯誤: {ex.Message}");
            }
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            try
            {
                lock (_lock)
                {
                    _connectedClients--;
                    _logger.LogInformation($"客戶端斷開連接: {Context.ConnectionId}, 剩餘連接數: {_connectedClients}");

                    // 如果沒有客戶端連接了，停止監控
                    if (_connectedClients <= 0)
                    {
                        StopMonitoring();
                        _connectedClients = 0; // 確保不會出現負數
                    }
                }

                await base.OnDisconnectedAsync(exception);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"處理客戶端斷開連接時發生錯誤: {Context.ConnectionId}");
            }
        }

        private async Task StartDatabaseMonitoring()
        {
            if (_isMonitoring)
            {
                _logger.LogInformation("已經在監控中，跳過啟動");
                return;
            }

            lock (_lock)
            {
                if (_isMonitoring) return;
                _isMonitoring = true;
            }

            try
            {
                _logger.LogInformation("開始資料庫監控");
                
                // 停止現有的監控
                StopMonitoring();

                // 確保 Service Broker 已啟用
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using (var command = new SqlCommand(@"
                        -- 確保資料庫設置正確
                        IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = 'SignalR' AND is_broker_enabled = 1)
                        BEGIN
                            ALTER DATABASE SignalR SET ENABLE_BROKER WITH ROLLBACK IMMEDIATE;
                        END
                        
                        IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = 'SignalR' AND is_trustworthy_on = 1)
                        BEGIN
                            ALTER DATABASE SignalR SET TRUSTWORTHY ON;
                        END

                        -- 確保 Service Broker 服務已啟動
                        IF NOT EXISTS (SELECT 1 FROM sys.dm_broker_activated_tasks WHERE database_id = DB_ID('SignalR'))
                        BEGIN
                            ALTER DATABASE SignalR SET NEW_BROKER WITH ROLLBACK IMMEDIATE;
                        END", connection))
                    {
                        await command.ExecuteNonQueryAsync();
                        _logger.LogInformation("資料庫設置已更新");
                    }

                    // 獲取當前最大的 ChargingID
                    using (var command = new SqlCommand(@"
                        SELECT MAX(ChargingID) 
                        FROM [SignalR].[dbo].[ChargingStorage]", connection))
                    {
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            _lastKnownChargingId = Convert.ToInt32(result);
                            _logger.LogInformation($"初始化最後已知的 ChargingID: {_lastKnownChargingId}");
                        }
                    }
                }

                // 啟動 SQL Dependency
                SqlDependency.Stop(_connectionString);
                SqlDependency.Start(_connectionString);
                _logger.LogInformation("SQL Dependency 服務已重新啟動");
                
                // 設置初始依賴
                await SetupDependency();
                _logger.LogInformation("資料庫監控已成功啟動");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "啟動監控時發生錯誤");
                _isMonitoring = false;
            }
        }

        private async Task SetupDependency()
        {
            try
            {
                _logger.LogInformation("設置新的依賴");
                
                // 關閉現有連接
                if (_connection != null)
                {
                    await _connection.CloseAsync();
                    _connection.Dispose();
                }

                // 創建新連接
                _connection = new SqlConnection(_connectionString);
                await _connection.OpenAsync();

                // 使用更簡單的查詢來監控變更
                var command = new SqlCommand(@"
                    SELECT ChargingID 
                    FROM [SignalR].[dbo].[ChargingStorage] WITH (NOLOCK)
                    ORDER BY ChargingID DESC", _connection);

                command.CommandTimeout = 60;
                command.Notification = null;

                _logger.LogInformation("創建 SQL Dependency");
                _dependency = new SqlDependency(command);
                _dependency.OnChange += async (sender, e) => await DependencyOnChange(sender, e);

                _logger.LogInformation("執行查詢以啟動監控");
                using (var reader = await command.ExecuteReaderAsync())
                {
                    // 只需要執行查詢，不需要處理結果
                    while (await reader.ReadAsync()) { }
                }

                _logger.LogInformation("SQL Dependency 設置完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "設置 SQL Dependency 時發生錯誤");
                throw;
            }
        }

        private async Task DependencyOnChange(object sender, SqlNotificationEventArgs e)
        {
            _logger.LogInformation($"收到資料庫變更通知: Type={e.Type}, Info={e.Info}, Source={e.Source}");

            try
            {
                // 檢查是否真的有新數據
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using (var command = new SqlCommand(@"
                        SELECT MAX(ChargingID) 
                        FROM [SignalR].[dbo].[ChargingStorage]", connection))
                    {
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            int currentMaxId = Convert.ToInt32(result);
                            if (currentMaxId > _lastKnownChargingId)
                            {
                                _logger.LogInformation($"檢測到新數據: 當前ID={currentMaxId}, 上次ID={_lastKnownChargingId}");
                                _lastKnownChargingId = currentMaxId;
                                
                                // 只有在確實有新數據時才發送更新
                                await SendDatabaseUpdateToAll();
                                _logger.LogInformation("成功發送數據庫更新");
                            }
                            else
                            {
                                _logger.LogInformation("收到通知但數據未變化，跳過更新");
                            }
                        }
                    }
                }

                // 重新設置監控
                if (sender is SqlDependency dependency)
                {
                    dependency.OnChange -= async (s, args) => await DependencyOnChange(s, args);
                }
                await SetupDependency();
                _logger.LogInformation("成功重新設置監控");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "處理 SQL Dependency 變更時發生錯誤");
                // 嘗試重新設置監控
                _ = SetupDependency();
            }
        }

        private void StopMonitoring()
        {
            try
            {
                if (_dependency != null)
                {
                    _dependency.OnChange -= async (s, e) => await DependencyOnChange(s, e);
                }

                if (_connection != null)
                {
                    _connection.Close();
                    _connection.Dispose();
                    _connection = null;
                }

                _dependency = null;
                SqlDependency.Stop(_connectionString);
                
                _logger.LogInformation("監控已停止");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止監控時發生錯誤");
            }
            finally
            {
                _isMonitoring = false;
            }
        }

        private async Task SendDatabaseUpdateToAll()
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using (var command = new SqlCommand(@"
                        SELECT TOP 10
                            ChargingID, SiteID, ReceivedTime, Datatime, 
                            Sequence, PowerStatus, PcsStatus, AcbStatus,
                            Soc, kWh, PCSPower, Sitepower
                        FROM [SignalR].[dbo].[ChargingStorage]
                        ORDER BY ChargingID DESC", connection))
                    {
                        using var reader = await command.ExecuteReaderAsync();
                        var data = new List<object>();
                        while (await reader.ReadAsync())
                        {
                            data.Add(new
                            {
                                ChargingID = reader.GetInt32(0),
                                SiteID = reader.GetInt32(1),
                                ReceivedTime = reader.GetDateTime(2),
                                Datatime = reader.GetDateTime(3),
                                Sequence = reader.GetInt32(4),
                                PowerStatus = reader.GetInt32(5),
                                PcsStatus = reader.GetInt32(6),
                                AcbStatus = reader.GetInt32(7),
                                Soc = reader.GetDecimal(8),
                                kWh = reader.GetDecimal(9),
                                PCSPower = reader.GetDecimal(10),
                                Sitepower = reader.GetDecimal(11)
                            });
                        }

                        if (data.Any())
                        {
                            await _hubContext.Clients.All.SendAsync("ReceiveDatabaseUpdate", data);
                            _logger.LogInformation($"成功發送數據庫更新，數據筆數: {data.Count}");
                        }
                        else
                        {
                            _logger.LogWarning("未找到任何數據");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "發送數據庫更新時發生錯誤");
            }
        }

        public async Task SendDatabaseUpdate()
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using (var command = new SqlCommand(@"
                        SELECT TOP 10
                            ChargingID, SiteID, ReceivedTime, Datatime, 
                            Sequence, PowerStatus, PcsStatus, AcbStatus,
                            Soc, kWh, PCSPower, Sitepower
                        FROM [SignalR].[dbo].[ChargingStorage]
                        ORDER BY ChargingID DESC", connection))
                    {
                        using var reader = await command.ExecuteReaderAsync();
                        var data = new List<object>();
                        while (await reader.ReadAsync())
                        {
                            data.Add(new
                            {
                                ChargingID = reader.GetInt32(0),
                                SiteID = reader.GetInt32(1),
                                ReceivedTime = reader.GetDateTime(2),
                                Datatime = reader.GetDateTime(3),
                                Sequence = reader.GetInt32(4),
                                PowerStatus = reader.GetInt32(5),
                                PcsStatus = reader.GetInt32(6),
                                AcbStatus = reader.GetInt32(7),
                                Soc = reader.GetDecimal(8),
                                kWh = reader.GetDecimal(9),
                                PCSPower = reader.GetDecimal(10),
                                Sitepower = reader.GetDecimal(11)
                            });
                        }

                        if (data.Any())
                        {
                            await Clients.All.SendAsync("ReceiveDatabaseUpdate", data);
                            _logger.LogInformation($"成功發送數據庫更新，數據筆數: {data.Count}");
                        }
                        else
                        {
                            _logger.LogWarning("未找到任何數據");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "發送數據庫更新時發生錯誤");
                throw;
            }
        }
    }
}