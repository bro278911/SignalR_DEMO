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
        private readonly IHubContext<ChatHub> _hubContext;
        private SqlDependency _dependency;
        private SqlConnection _connection;
        private bool _isMonitoring = false;
        private readonly string _connectionString;

        public DatabaseMonitorService(
            IConfiguration configuration,
            ILogger<DatabaseMonitorService> logger,
            IHubContext<ChatHub> hubContext)
        {
            _configuration = configuration;
            _logger = logger;
            _hubContext = hubContext;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("DatabaseMonitorService 正在啟動");
            await StartDatabaseMonitoring();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("DatabaseMonitorService 正在停止");
            StopMonitoring();
            return Task.CompletedTask;
        }

        private async Task StartDatabaseMonitoring()
        {
            if (_isMonitoring)
            {
                return;
            }

            try
            {
                _connection = new SqlConnection(_connectionString);
                await _connection.OpenAsync();

                using (var command = new SqlCommand(
                    "SELECT is_broker_enabled FROM sys.databases WHERE name = 'Cloud_EMS'",
                    _connection))
                {
                    var isBrokerEnabled = (bool)await command.ExecuteScalarAsync();
                    if (!isBrokerEnabled)
                    {
                        throw new Exception("Database Service Broker is not enabled!");
                    }
                }

                SqlDependency.Start(_connectionString);
                await SetupDependency();
                _isMonitoring = true;
                
                _logger.LogInformation("資料庫監控已成功啟動");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "啟動監控時發生錯誤");
                _isMonitoring = false;
                throw;
            }
        }

        private async Task SetupDependency()
        {
            try
            {
                var query = @"
                    SELECT TOP 100 *
                    FROM [Cloud_EMS].[dbo].[ChargingStorage] WITH (NOLOCK)
                    ORDER BY ChargingID DESC, ReceivedTime DESC";

                using (var command = new SqlCommand(query, _connection))
                {
                    command.CommandTimeout = 60;
                    command.Notification = null;

                    _dependency = new SqlDependency(command);
                    _dependency.OnChange += DependencyOnChange;

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
                        await _hubContext.Clients.All.SendAsync("ReceiveDatabaseUpdate", data);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SetupDependency 發生錯誤");
                throw;
            }
        }

        private void DependencyOnChange(object sender, SqlNotificationEventArgs e)
        {
            _logger.LogInformation($"收到資料庫變更通知: Type={e.Type}, Info={e.Info}, Source={e.Source}");
            
            if (e.Type == SqlNotificationType.Change)
            {
                _ = SetupDependency();
            }
        }

        private void StopMonitoring()
        {
            try
            {
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StopMonitoring 發生錯誤");
            }
        }
    }
}
