
namespace Opc.Ua.Cloud.Publisher.Models
{
    using System.Collections.Generic;

    public class SessionModel
    {
        public string EndpointUrl { get; set; }

        public string UserName { get; set; }

        public string Password { get; set; }

        public string StatusMessage { get; set; }

        public List<string> Namespaces { get; set; } = new();
    }
}