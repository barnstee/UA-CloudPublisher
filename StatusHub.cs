
namespace UA.MQTT.Publisher
{
    using Microsoft.AspNetCore.SignalR;
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading.Tasks;

    public class StatusHub : Hub
    {
        // this is our SignalR Status Hub
    }

    public class StatusHubClient
    {
        private readonly IHubContext<StatusHub> _hubContext;

        private Dictionary<string, Tuple<string, bool>> TableEntries { get; set; } = new Dictionary<string, Tuple<string, bool>>();

        private Dictionary<string, string[]> ChartEntries { get; set; } = new Dictionary<string, string[]>();

        public StatusHubClient(IHubContext<StatusHub> hubContext)
        {
            _hubContext = hubContext;

            _ = Task.Run(() => SendMessageViaSignalR());
        }

        public void AddOrUpdateTableEntry(string key, string value, bool addToChart = false)
        {
            lock (TableEntries)
            {
                if (TableEntries.ContainsKey(key))
                {
                    TableEntries[key] = new Tuple<string, bool>(value, addToChart);
                }
                else
                {
                    TableEntries.Add(key, new Tuple<string, bool>(value, addToChart));
                }
            }
        }

        public void AddChartEntry(string timeAxisEntry, string[] valueAxisEntries)
        {
            ChartEntries.Add(timeAxisEntry, valueAxisEntries);
        }

        private async Task SendMessageViaSignalR()
        {
            while (true)
            {
                await Task.Delay(1000).ConfigureAwait(false);

                lock (TableEntries)
                {
                    foreach (KeyValuePair<string, Tuple<string, bool>> entry in TableEntries)
                    {
                        if (entry.Value.Item2 == true)
                        {
                            _hubContext.Clients.All.SendAsync("addDatasetToChart", entry.Key).GetAwaiter().GetResult();
                        }
                    }

                    foreach (KeyValuePair<string, string[]> entry in ChartEntries)
                    {
                        _hubContext.Clients.All.SendAsync("addDataToChart", entry.Key, entry.Value).GetAwaiter().GetResult();
                    }
                    ChartEntries.Clear();

                    CreateTable();
                }
            }
        }

        private void CreateTable()
        {
            // create HTML table
            StringBuilder sb = new StringBuilder();
            sb.Append("<table width='800px' cellpadding='3' cellspacing='3'>");

            // header
            sb.Append("<tr>");
            sb.Append("<th><b>Name</b></th>");
            sb.Append("<th><b>Latest Value</b></th>");
            sb.Append("</tr>");

            // rows
            lock (TableEntries)
            {
                foreach (KeyValuePair<string, Tuple<string, bool>> item in TableEntries)
                {
                    sb.Append("<tr>");
                    sb.Append("<td style='width:400px'>" + item.Key + "</td>");
                    sb.Append("<td style='width:200px'>" + item.Value.Item1 + "</td>");
                    sb.Append("</tr>");
                }
            }

            sb.Append("</table>");

            _hubContext.Clients.All.SendAsync("addTable", sb.ToString()).GetAwaiter().GetResult();
        }
    }
}
