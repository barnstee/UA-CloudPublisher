
namespace Opc.Ua.Cloud.Publisher
{
    using Opc.Ua;
    using Opc.Ua.Client;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using System;
    using System.Collections.Concurrent;
    using System.Security.Cryptography.X509Certificates;
    using System.Threading;
    using System.Threading.Tasks;

    public class OpcSessionCacheData
    {
        public Session OPCSession { get; set; }

        public string EndpointURL { get; set; }

        public string Username { get; set; }

        public string Password { get; set; }

        public OpcSessionCacheData()
        {
            OPCSession = null;
            EndpointURL = string.Empty;
            Username = string.Empty;
            Password = string.Empty;
        }
    }

    public class OpcSessionHelper
    {
        public ConcurrentDictionary<string, OpcSessionCacheData> OpcSessionCache = new ConcurrentDictionary<string, OpcSessionCacheData>();

        private readonly ApplicationConfiguration _configuration = null;

        private readonly IUAApplication _app;

        private X509Certificate2 _appCert = null;

        public OpcSessionHelper(IUAApplication app)
        {
            _app = app;
            _configuration = app.UAApplicationInstance.ApplicationConfiguration;
            _appCert = _configuration.SecurityConfiguration.ApplicationCertificate.Certificate;
        }

        public X509Certificate2 GetAppCert()
        {
            return _appCert;
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

            OpcSessionCacheData entry = null;
            if (OpcSessionCache.TryGetValue(sessionID, out entry))
            {
                if ((entry != null) && (entry.OPCSession != null))
                {
                    if (entry.OPCSession.Connected)
                    {
                        return entry.OPCSession;
                    }

                    try
                    {
                        entry.OPCSession.Close(500);
                    }
                    catch (Exception)
                    {
                        // do nothing
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

            ConfiguredEndpoint configuredEndpoint = new ConfiguredEndpoint(null, selectedEndpoint, EndpointConfiguration.Create(_configuration));


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
                _configuration.ApplicationName,
                (uint)_configuration.ClientConfiguration.DefaultSessionTimeout,
                userIdentity,
                null
            ).ConfigureAwait(false);

            if (session != null)
            {
                // enable diagnostics
                session.ReturnDiagnostics = DiagnosticsMasks.All;

                // Update our cache data
                if (OpcSessionCache.TryGetValue(sessionID, out entry))
                {
                    if (string.Equals(entry.EndpointURL, endpointURL, StringComparison.InvariantCultureIgnoreCase))
                    {
                        OpcSessionCacheData newValue = new OpcSessionCacheData
                        {
                            EndpointURL = entry.EndpointURL,
                            OPCSession = session,
                            Username = username,
                            Password = password
                        };
                        OpcSessionCache.TryUpdate(sessionID, newValue, entry);
                    }
                }
            }

            return session;
        }
    }
}