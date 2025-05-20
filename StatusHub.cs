
namespace Opc.Ua.Cloud.Publisher
{
    using Microsoft.AspNetCore.SignalR;
    using System.Threading.Tasks;

    public class StatusHub : Hub
    {
        // this is our SignalR Status Hub
    }

    public class StatusHubClient
    {
        private readonly IHubContext<StatusHub> _hubContext;

        public StatusHubClient(IHubContext<StatusHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public async Task UpdateClientProgressAsync(int percentage)
        {
            await _hubContext.Clients.All.SendAsync("updateProgress", percentage).ConfigureAwait(false);
        }
    }
}
