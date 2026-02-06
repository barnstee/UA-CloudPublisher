namespace Opc.Ua.Cloud.Publisher.Models
{
    using System.Collections.Generic;

    public class TemplateReplacementModel
    {
        public string FileName { get; set; }
        public string EndpointUrl { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string FileContent { get; set; }
        public Dictionary<string, string> TemplateValues { get; set; } = new Dictionary<string, string>();
    }
}
