using Microsoft.AspNetCore.SignalR;

namespace signalR_test.Hubs
{
    public class DatabaseHub : Hub
    {
        public async Task NotifyDatabaseChange(string table, string operation)
        {
            await Clients.All.SendAsync("DatabaseChanged", table, operation);
        }
    }
}
