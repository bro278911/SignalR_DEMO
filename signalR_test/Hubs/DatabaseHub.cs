using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using signalR_test.Models;
using System.Threading.Tasks;

namespace signalR_test.Hubs
{
    public class DatabaseHub : Hub
    {
        private readonly ILogger<DatabaseHub> _logger;

        public DatabaseHub(ILogger<DatabaseHub> logger)
        {
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation($"Client connected: {Context.ConnectionId}");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogInformation($"Client disconnected: {Context.ConnectionId}. Exception: {exception?.Message}");
            await base.OnDisconnectedAsync(exception);
        }

        public async Task SendMessage(string message)
        {
            _logger.LogInformation($"Received message: {message}");
            await Clients.All.SendAsync("ReceiveMessage", message);
        }

        public async Task NotifyDatabaseChange(string table, string operation, object data)
        {
            _logger.LogInformation($"Notifying database change: Table={table}, Operation={operation}");
            await Clients.All.SendAsync("DatabaseChanged", table, operation, data);
        }
    }
}
