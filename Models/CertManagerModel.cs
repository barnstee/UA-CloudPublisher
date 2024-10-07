using Microsoft.AspNetCore.Mvc.Rendering;

namespace Opc.Ua.Cloud.Publisher.Models
{
    public class CertManagerModel
    {
        public SelectList Certs { get; set; }

        public string Encrypt { get; set; }
    }
}
