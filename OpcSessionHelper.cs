
namespace Opc.Ua.Cloud.Publisher
{
    using Opc.Ua;
    using Opc.Ua.Client;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using System;
    using System.Collections.Concurrent;
    using System.Threading;
    using System.Threading.Tasks;

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

        private readonly IUAApplication _app;

        public OpcSessionHelper(IUAApplication app)
        {
            _app = app;
            _configuration = app.UAApplicationInstance.ApplicationConfiguration;
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

        public async Task<Session> GetSessionAsync(string sessionID, string endpointURL, string username = null, string password = null)
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

            EndpointDescription selectedEndpoint = null;
            ITransportWaitingConnection connection = null;
            if (Settings.Instance.UseReverseConnect)
            {
                connection = await _app.ReverseConnectManager.WaitForConnection(new Uri(endpointURL), null, new CancellationTokenSource(30_000).Token).ConfigureAwait(false);
                if (connection == null)
                {
                    throw new ServiceResultException(StatusCodes.BadTimeout, "Waiting for a reverse connection timed out after 30 seconds.");
                }

                selectedEndpoint = CoreClientUtils.SelectEndpoint(_configuration, connection, true);
            }
            else
            {
                selectedEndpoint = CoreClientUtils.SelectEndpoint(endpointURL, true);
            }

            ConfiguredEndpoint configuredEndpoint = new ConfiguredEndpoint(null, selectedEndpoint, EndpointConfiguration.Create());


            uint timeout = (uint)_configuration.ClientConfiguration.DefaultSessionTimeout;

            UserIdentity userIdentity = null;
            if (username == null)
            {
                userIdentity = new UserIdentity(new AnonymousIdentityToken());
            }
            else
            {
                userIdentity = new UserIdentity(username, password);
            }

            Session session = await Session.Create(
                _configuration,
                configuredEndpoint,
                true,
                false,
                sessionID,
                60000,
                userIdentity,
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