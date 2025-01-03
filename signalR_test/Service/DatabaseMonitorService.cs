using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using signalR_test.Hubs;

namespace signalR_test.Service
{
    public class DatabaseMonitorService : BackgroundService
    {
        private readonly IHubContext<DatabaseHub> _hubContext;
        private readonly IConfiguration _configuration;
        private SqlDependency _dependency;

        public DatabaseMonitorService(
            IHubContext<DatabaseHub> hubContext,
            IConfiguration configuration)
        {
            _hubContext = hubContext;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 啟用 SQL 依賴服務
            SqlDependency.Start(_configuration.GetConnectionString("DefaultConnection"));

            while (!stoppingToken.IsCancellationRequested)
            {
                await MonitorDatabase();
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }

        private async Task MonitorDatabase()
        {
            using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                await connection.OpenAsync();

                using (var command = new SqlCommand(
                    "SELECT * FROM [Cloud_EMS].[dbo].[ChargingStorage]", connection))
                {
                    // 建立資料庫依賴
                    _dependency = new SqlDependency(command);
                    _dependency.OnChange += async (sender, e) =>
                    {
                        if (e.Type == SqlNotificationType.Change)
                        {
                            // 發送通知給所有連接的客戶端
                            await _hubContext.Clients.All.SendAsync(
                                "DatabaseChanged",
                                "YourTable",
                                e.Info.ToString()
                            );
                        }
                    };

                    // 執行查詢以啟動監聽
                    await command.ExecuteReaderAsync();
                }
            }
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            SqlDependency.Stop(_configuration.GetConnectionString("DefaultConnection"));
            await base.StopAsync(stoppingToken);
        }
    }
}
