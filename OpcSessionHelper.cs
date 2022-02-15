
namespace UA.MQTT.Publisher
{
    using Opc.Ua;
    using Opc.Ua.Client;
    using System;
    using System.Collections.Concurrent;
    using System.Threading.Tasks;
    using UA.MQTT.Publisher.Interfaces;

    public class OpcSessionCacheData
    {
        public bool Trusted { get; set; }

        public Session OPCSession { get; set; }

        public string CertThumbprint { get; set; }

        public string EndpointURL { get; set; }

        public OpcSessionCacheData()
        {
            Trusted = false;
            EndpointURL = string.Empty;
            CertThumbprint = string.Empty;
            OPCSession = null;
        }
    }

    public class OpcSessionHelper
    {
        public ConcurrentDictionary<string, OpcSessionCacheData> OpcSessionCache = new ConcurrentDictionary<string, OpcSessionCacheData>();
        
        private readonly ApplicationConfiguration _configuration = null;

        public OpcSessionHelper(IUAApplication app)
        {
            _configuration = app.GetAppConfig();
        }
        
        public void Disconnect(string sessionID)
        {
            OpcSessionCacheData entry;
            if (OpcSessionCache.TryRemove(sessionID, out entry))
            {
                try
                {
                    if (entry.OPCSession != null)
                    {
                        entry.OPCSession.Close();
                    }
                }
                catch
                {
                    // do nothing
                }
            }
        }

        public async Task<Session> GetSessionAsync(string sessionID, string endpointURL, bool enforceTrust = false)
        {
            if (string.IsNullOrEmpty(sessionID) || string.IsNullOrEmpty(endpointURL))
            {
                return null;
            }

            OpcSessionCacheData entry;
            if (OpcSessionCache.TryGetValue(sessionID, out entry))
            {
                if (entry.OPCSession != null)
                {
                    if (entry.OPCSession.Connected)
                    {
                        return entry.OPCSession;
                    }

                    try
                    {
                        entry.OPCSession.Close(500);
                    }
                    catch
                    {
                    }
                    entry.OPCSession = null;
                }
            }
            else
            {
                // create a new entry
                OpcSessionCacheData newEntry = new OpcSessionCacheData { EndpointURL = endpointURL };
                OpcSessionCache.TryAdd(sessionID, newEntry);
            }

            Uri endpointURI = new Uri(endpointURL);
            EndpointDescriptionCollection endpointCollection = DiscoverEndpoints(_configuration, endpointURI, 10);
            EndpointDescription selectedEndpoint = SelectUaTcpEndpoint(endpointCollection);
            EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(_configuration);
            ConfiguredEndpoint endpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointConfiguration);

            Session session = await Session.Create(
                _configuration,
                endpoint,
                true,
                false,
                sessionID,
                60000,
                new UserIdentity(new AnonymousIdentityToken()),
                null).ConfigureAwait(false);

            if (session != null)
            {
                // Update our cache data
                if (OpcSessionCache.TryGetValue(sessionID, out entry))
                {
                    if (string.Equals(entry.EndpointURL, endpointURL, StringComparison.InvariantCultureIgnoreCase))
                    {
                        OpcSessionCacheData newValue = new OpcSessionCacheData
                        {
                            CertThumbprint = entry.CertThumbprint,
                            EndpointURL = entry.EndpointURL,
                            Trusted = entry.Trusted,
                            OPCSession = session
                        };
                        OpcSessionCache.TryUpdate(sessionID, newValue, entry);
                    }
                }
            }
            
            return session;
        }

        private EndpointDescriptionCollection DiscoverEndpoints(ApplicationConfiguration config, Uri discoveryUrl, int timeout)
        {
            EndpointConfiguration configuration = EndpointConfiguration.Create(config);
            configuration.OperationTimeout = timeout;

            using (DiscoveryClient client = DiscoveryClient.Create(
                discoveryUrl,
                EndpointConfiguration.Create(config)))
            {
                EndpointDescriptionCollection endpoints = client.GetEndpoints(null);
                return ReplaceLocalHostWithRemoteHost(endpoints, discoveryUrl);
            }
        }

        private EndpointDescription SelectUaTcpEndpoint(EndpointDescriptionCollection endpointCollection)
        {
            EndpointDescription bestEndpoint = null;
            foreach (EndpointDescription endpoint in endpointCollection)
            {
                if (endpoint.TransportProfileUri == Profiles.UaTcpTransport)
                {
                    if ((bestEndpoint == null) ||
                        (endpoint.SecurityLevel > bestEndpoint.SecurityLevel))
                    {
                        bestEndpoint = endpoint;
                    }
                }
            }

            return bestEndpoint;
        }

        private EndpointDescriptionCollection ReplaceLocalHostWithRemoteHost(EndpointDescriptionCollection endpoints, Uri discoveryUrl)
        {
            EndpointDescriptionCollection updatedEndpoints = endpoints;

            foreach (EndpointDescription endpoint in updatedEndpoints)
            {
                endpoint.EndpointUrl = Utils.ReplaceLocalhost(endpoint.EndpointUrl, discoveryUrl.DnsSafeHost);

                StringCollection updatedDiscoveryUrls = new StringCollection();
                foreach (string url in endpoint.Server.DiscoveryUrls)
                {
                    updatedDiscoveryUrls.Add(Utils.ReplaceLocalhost(url, discoveryUrl.DnsSafeHost));
                }

                endpoint.Server.DiscoveryUrls = updatedDiscoveryUrls;
            }

            return updatedEndpoints;
        }
    }
}