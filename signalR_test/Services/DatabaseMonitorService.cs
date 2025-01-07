using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using System.Data;
using signalR_test.Hubs;

namespace signalR_test.Services
{
    public class DatabaseMonitorService : IHostedService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<DatabaseMonitorService> _logger;
        private readonly IHubContext<DatabaseHub> _hubContext;
        private SqlDependency _dependency;
        private SqlConnection _connection;
        private readonly string _connectionString;

        public DatabaseMonitorService(
            IConfiguration configuration,
            ILogger<DatabaseMonitorService> logger,
            IHubContext<DatabaseHub> hubContext)
        {
            _configuration = configuration;
            _logger = logger;
            _hubContext = hubContext;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("DatabaseMonitorService 正在啟動");
            try
            {
                // 檢查資料庫設置
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    using (var command = new SqlCommand(@"
                        SELECT 
                            DB_NAME() as DatabaseName,
                            DATABASEPROPERTYEX(DB_NAME(), 'IsBrokerEnabled') as IsBrokerEnabled,
                            DATABASEPROPERTYEX(DB_NAME(), 'IsTrustworthy') as IsTrustworthy,
                            HAS_PERMS_BY_NAME(null, null, 'SUBSCRIBE QUERY NOTIFICATIONS') as HasSubscribePermission
                        ", connection))
                    {
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                _logger.LogInformation($@"資料庫設置:
                                    DatabaseName: {reader["DatabaseName"]}
                                    IsBrokerEnabled: {reader["IsBrokerEnabled"]}
                                    IsTrustworthy: {reader["IsTrustworthy"]}
                                    HasSubscribePermission: {reader["HasSubscribePermission"]}");
                            }
                        }
                    }
                }

                SqlDependency.Start(_connectionString);
                await StartDatabaseMonitoring();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "啟動 SQL Dependency 失敗");
                throw;
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("DatabaseMonitorService 正在停止");
            SqlDependency.Stop(_connectionString);
            _connection?.Close();
            return Task.CompletedTask;
        }

        private async Task StartDatabaseMonitoring()
        {
            _connection = new SqlConnection(_connectionString);
            await _connection.OpenAsync();
            
            RegisterDependency();
        }

        private void RegisterDependency()
        {
            var command = new SqlCommand(
                @"SELECT [Id], [Message], [CreatedAt] 
                  FROM [TestMessages]",
                _connection);

            command.Notification = null;

            _dependency = new SqlDependency(command);
            _dependency.OnChange += async (sender, e) =>
            {
                if (e.Type == SqlNotificationType.Change)
                {
                    _logger.LogInformation($"檢測到數據變更: {e.Info}");
                    await _hubContext.Clients.All.SendAsync("DatabaseChanged", "TestMessages", e.Info.ToString(), DateTime.Now);
                }
                
                // 重新註冊依賴，因為它是一次性的
                RegisterDependency();
            };

            // 執行命令以開始監聽
            using (var reader = command.ExecuteReader()) { }
        }
    }
}
