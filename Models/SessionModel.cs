
namespace Opc.Ua.Cloud.Publisher.Models
{
    public class SessionModel
    {
        public string SessionId { get; set; }

        public string EndpointUrl { get; set; }

        public string UserName { get; set; }

        public string Password { get; set; }

        public string StatusMessage { get; set; }
    }
}