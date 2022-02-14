
namespace UA.MQTT.Publisher
{
    using Microsoft.AspNetCore.SignalR;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading.Tasks;

    public class StatusHub : Hub
    {
        public Dictionary<string, string> TableEntries { get; set; }

        public Dictionary<string, string[]> ChartEntries { get; set; }

        public StatusHub()
        {
            TableEntries = new Dictionary<string, string>();
            ChartEntries = new Dictionary<string, string[]>();

            Task.Run(() => SendMessageViaSignalR());
        }

        private async Task SendMessageViaSignalR()
        {
            while (true)
            {
                await Task.Delay(3000).ConfigureAwait(false);

                lock (TableEntries)
                {
                    foreach (string displayName in TableEntries.Keys)
                    {
                        Clients?.All.SendAsync("addDatasetToChart", displayName).GetAwaiter().GetResult();
                    }

                    foreach (KeyValuePair<string, string[]> entry in ChartEntries)
                    {
                        Clients?.All.SendAsync("addDataToChart", entry.Key, entry.Value).GetAwaiter().GetResult();
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
            foreach (KeyValuePair<string, string> item in TableEntries)
            {
                sb.Append("<tr>");
                sb.Append("<td style='width:400px'>" + item.Key + "</td>");
                sb.Append("<td style='width:200px'>" + item.Value + "</td>");
                sb.Append("</tr>");
            }

            sb.Append("</table>");

            Clients?.All.SendAsync("addTable", sb.ToString()).GetAwaiter().GetResult();
        }
    }
}
