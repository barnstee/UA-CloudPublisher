namespace Opc.Ua.Cloud
{
    using Opc.Ua.Configuration;
    using Serilog;
    using System.Threading.Tasks;

    public class ApplicationMessageDlg : IApplicationMessageDlg
    {
        private string _message = string.Empty;

        public override void Message(string text, bool ask)
        {
            _message = text;
        }

        public override Task<bool> ShowAsync()
        {
            Log.Logger.Information(_message);

            // always return yes
            return Task.FromResult(true);
        }
    }
}
