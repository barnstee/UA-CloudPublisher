
namespace Opc.Ua.Cloud.Publisher
{
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Opc.Ua;
    using Opc.Ua.Client;
    using Opc.Ua.Client.ComplexTypes;
    using Opc.Ua.Cloud.Publisher.Interfaces;
    using Opc.Ua.Cloud.Publisher.Models;
    using Opc.Ua.Gds.Client;
    using Opc.Ua.Security.Certificates;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    public class UAClient : IUAClient
    {
        private const uint WoTAssetConnectionManagement = 31;
        private const uint WoTAssetConnectionManagement_CreateAsset = 32;
        private const uint WoTAssetConnectionManagement_DeleteAsset = 35;

        private readonly IUAApplication _app;
        private readonly ILogger _logger;
        private readonly ILoggerFactory _loggerFactory;

        private IMessageSource _trigger;

        private List<ISession> _sessions = new List<ISession>();
        private object _sessionLock = new object();

        private List<SessionReconnectHandler> _reconnectHandlers = new List<SessionReconnectHandler>();
        private object _reconnectHandlersLock = new object();

        private List<PeriodicPublishing> _periodicPublishingList = new List<PeriodicPublishing>();
        private object _periodicPublishingListLock = new object();

        private Dictionary<string, uint> _missedKeepAlives = new Dictionary<string, uint>();
        private object _missedKeepAlivesLock = new object();

        private Dictionary<string, EndpointDescription> _endpointDescriptionCache = new Dictionary<string, EndpointDescription>();
        private object _endpointDescriptionCacheLock = new object();

        private readonly Dictionary<ISession, ComplexTypeSystem> _complexTypeList = new Dictionary<ISession, ComplexTypeSystem>();

        public UAClient(
            IUAApplication app,
            ILoggerFactory loggerFactory,
            IMessageSource trigger)
        {
            _logger = loggerFactory.CreateLogger("UAClient");
            _loggerFactory = loggerFactory;
            _app = app;
            _trigger = trigger;
        }

        public void Dispose()
        {
            try
            {
                UnpublishAllNodes(false);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failure while unpublishing all nodes.");
            }
        }

        private Session FindSession(string endpointUrl)
        {
            EndpointDescription selectedEndpoint;
            try
            {
                lock (_endpointDescriptionCacheLock)
                {
                    if (_endpointDescriptionCache.ContainsKey(endpointUrl))
                    {
                        selectedEndpoint = _endpointDescriptionCache[endpointUrl];
                    }
                    else
                    {
                        // use a discovery client to connect to the server and discover all its endpoints, then pick the one with the highest security
                        selectedEndpoint = CoreClientUtils.SelectEndpointAsync(_app.UAApplicationInstance.ApplicationConfiguration, endpointUrl, true, _app.Telemetry).GetAwaiter().GetResult();

                        // add to cache
                        _endpointDescriptionCache[endpointUrl] = selectedEndpoint;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cannot reach server on endpoint {endpointUrl}. Please make sure your OPC UA server is running and accessible.", endpointUrl);
                return null;
            }

            if (selectedEndpoint == null)
            {
                // could not get the requested endpoint
                return null;
            }

            // check if there is already a session for the requested endpoint
            lock (_sessionLock)
            {
                ConfiguredEndpoint configuredEndpoint = new ConfiguredEndpoint(
                    null,
                    selectedEndpoint,
                    EndpointConfiguration.Create()
                );

                foreach (Session session in _sessions)
                {
                    if ((session.ConfiguredEndpoint.EndpointUrl == configuredEndpoint.EndpointUrl) ||
                        (session.ConfiguredEndpoint.EndpointUrl.ToString() == endpointUrl))
                    {
                        // return the existing session
                        return session;
                    }
                }
            }

            return null;
        }

        public async Task Disconnect(string endpointUrl)
        {
            Session existingSession = FindSession(endpointUrl);
            if ((existingSession != null) && (existingSession.SubscriptionCount == 0))
            {
                await existingSession.CloseAsync().ConfigureAwait(false);
                _sessions.Remove(existingSession);
                _complexTypeList.Remove(existingSession);
                Diagnostics.Singleton.Info.NumberOfOpcSessionsConnected--;
                existingSession = null;
            }
        }

        private async Task<ISession> ConnectSessionAsync(string endpointUrl, string username, string password)
        {
            // check if the required session is already available
            ISession existingSession = FindSession(endpointUrl);
            if (existingSession != null)
            {
                if (existingSession.Connected)
                {
                    return existingSession;
                }
                else
                {
                    await existingSession.CloseAsync().ConfigureAwait(false);
                }
            }

            EndpointDescription selectedEndpoint = null;
            ITransportWaitingConnection connection = null;
            if (Settings.Instance.UseReverseConnect)
            {
                _logger.LogInformation("Waiting for reverse connection from {0}", endpointUrl);
                connection = await _app.ReverseConnectManager.WaitForConnectionAsync(new Uri(endpointUrl), null, new CancellationTokenSource(30_000).Token).ConfigureAwait(false);
                if (connection == null)
                {
                    throw new ServiceResultException(StatusCodes.BadTimeout, "Waiting for a reverse connection timed out after 30 seconds.");
                }

                selectedEndpoint = await CoreClientUtils.SelectEndpointAsync(_app.UAApplicationInstance.ApplicationConfiguration, connection, true, _app.Telemetry).ConfigureAwait(false);
            }
            else
            {
                selectedEndpoint = await CoreClientUtils.SelectEndpointAsync(_app.UAApplicationInstance.ApplicationConfiguration, endpointUrl, true, _app.Telemetry).ConfigureAwait(false);
            }

            ConfiguredEndpoint configuredEndpoint = new ConfiguredEndpoint(null, selectedEndpoint, EndpointConfiguration.Create(_app.UAApplicationInstance.ApplicationConfiguration));
            _logger.LogInformation("Connecting session on endpoint {endpointUrl}.", configuredEndpoint.EndpointUrl);

            uint timeout = (uint)_app.UAApplicationInstance.ApplicationConfiguration.ClientConfiguration.DefaultSessionTimeout;

            _logger.LogInformation("Creating session for endpoint {endpointUrl} with timeout of {timeout} ms.",
                configuredEndpoint.EndpointUrl,
                timeout);

            UserIdentity userIdentity = null;
            if (username == null)
            {
                userIdentity = new UserIdentity(new AnonymousIdentityToken());
            }
            else
            {
                userIdentity = new UserIdentity(username, Encoding.UTF8.GetBytes(password));
            }

            ISession newSession = null;
            try
            {
                newSession = await new DefaultSessionFactory(_app.Telemetry).CreateAsync(
                    _app.UAApplicationInstance.ApplicationConfiguration,
                    connection,
                    configuredEndpoint,
                    connection == null,
                    false,
                    _app.UAApplicationInstance.ApplicationConfiguration.ApplicationName,
                    timeout,
                    userIdentity,
                    null
                ).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Session creation to endpoint {endpointUrl} failed. Please verify that the OPC UA server for the specified endpoint is accessible.",
                     configuredEndpoint.EndpointUrl);

                return null;
            }

            _logger.LogInformation("Session successfully created with Id {session}.", newSession.SessionId);
            if (!selectedEndpoint.EndpointUrl.Equals(configuredEndpoint.EndpointUrl.OriginalString, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("The Server has updated the EndpointUrl to {endpointUrl}", selectedEndpoint.EndpointUrl);
            }

            // enable diagnostics
            newSession.ReturnDiagnostics = DiagnosticsMasks.All;

            // register keep alive callback
            newSession.KeepAlive += KeepAliveHandler;

            // enable subscriptions transfer
            newSession.DeleteSubscriptionsOnClose = false;
            newSession.TransferSubscriptionsOnReconnect = true;

            // add the session to our list
            lock (_sessionLock)
            {
                _sessions.Add(newSession);
                Diagnostics.Singleton.Info.NumberOfOpcSessionsConnected++;
            }

            // load complex type system
            try
            {
                if (!_complexTypeList.ContainsKey(newSession))
                {
                    _complexTypeList.Add(newSession, new ComplexTypeSystem(newSession));
                }

                await _complexTypeList[newSession].LoadAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load complex type system for session!");
            }

            return newSession;
        }

        public void UnpublishAllNodes(bool updatePersistencyFile = true)
        {
            // loop through all sessions
            lock (_sessionLock)
            {
                foreach (PeriodicPublishing heartbeat in _periodicPublishingList)
                {
                    heartbeat.Stop();
                    heartbeat.Dispose();
                }
                _periodicPublishingList.Clear();

                while (_sessions.Count > 0)
                {
                    ISession session = _sessions[0];
                    while (session.SubscriptionCount > 0)
                    {
                        Subscription subscription = session.Subscriptions.First();
                        while (subscription.MonitoredItemCount > 0)
                        {
                            subscription.RemoveItem(subscription.MonitoredItems.First());
                            subscription.ApplyChangesAsync().GetAwaiter().GetResult();
                            Diagnostics.Singleton.Info.NumberOfOpcMonitoredItemsMonitored--;
                        }

                        session.RemoveSubscriptionAsync(subscription).GetAwaiter().GetResult();
                        Diagnostics.Singleton.Info.NumberOfOpcSubscriptionsConnected--;
                    }

                    string endpoint = session.ConfiguredEndpoint.EndpointUrl.AbsoluteUri;
                    session.CloseAsync().GetAwaiter().GetResult();
                    _sessions.Remove(session);
                    _complexTypeList.Remove(session);
                    Diagnostics.Singleton.Info.NumberOfOpcSessionsConnected--;
                    session = null;

                    _logger.LogInformation("Session to endpoint {endpoint} closed successfully.", endpoint);
                }
            }

            // update our persistency
            if (updatePersistencyFile)
            {
                PersistPublishedNodes();
            }

            // make sure our UA Server telemetry is zeroed out
            Diagnostics.Singleton.Info.NumberOfOpcSessionsConnected = 0;
            Diagnostics.Singleton.Info.NumberOfOpcSubscriptionsConnected = 0;
            Diagnostics.Singleton.Info.NumberOfOpcMonitoredItemsMonitored = 0;
        }

        private async Task<Subscription> CreateSubscription(ISession session, int publishingInterval)
        {
            Subscription subscription = new Subscription(session.DefaultSubscription)
            {
                PublishingInterval = publishingInterval,
            };

            // add needs to happen before create to set the Session property
            session.AddSubscription(subscription);
            await subscription.CreateAsync().ConfigureAwait(false);

            Diagnostics.Singleton.Info.NumberOfOpcSubscriptionsConnected++;

            _logger.LogInformation("Created subscription with id {id} on endpoint {endpointUrl}.",
                subscription.Id,
                session.Endpoint.EndpointUrl);

            if (publishingInterval != subscription.PublishingInterval)
            {
                _logger.LogInformation("Publishing interval: requested: {requestedPublishingInterval}; revised: {revisedPublishingInterval}",
                    publishingInterval,
                    subscription.PublishingInterval);
            }

            return subscription;
        }

        private void KeepAliveHandler(ISession session, KeepAliveEventArgs eventArgs)
        {
            if (eventArgs != null && session != null && session.ConfiguredEndpoint != null)
            {
                try
                {
                    string endpoint = session.ConfiguredEndpoint.EndpointUrl.AbsoluteUri;

                    lock (_missedKeepAlivesLock)
                    {
                        if (!ServiceResult.IsGood(eventArgs.Status))
                        {
                            _logger.LogWarning("Session endpoint: {endpointUrl} has Status: {status}", session.ConfiguredEndpoint.EndpointUrl, eventArgs.Status);
                            _logger.LogInformation("Outstanding requests: {outstandingRequestCount}, Defunct requests: {defunctRequestCount}", session.OutstandingRequestCount, session.DefunctRequestCount);
                            _logger.LogInformation("Good publish requests: {goodPublishRequestCount}, KeepAlive interval: {keepAliveInterval}", session.GoodPublishRequestCount, session.KeepAliveInterval);
                            _logger.LogInformation("SessionId: {sessionId}", session.SessionId);
                            _logger.LogInformation("Session State: {connected}", session.Connected);

                            if (session.Connected)
                            {
                                // add a new entry, if required
                                if (!_missedKeepAlives.ContainsKey(endpoint))
                                {
                                    _missedKeepAlives.Add(endpoint, 0);
                                }

                                _missedKeepAlives[endpoint]++;
                                _logger.LogInformation("Missed Keep-Alives: {missedKeepAlives}", _missedKeepAlives[endpoint]);
                            }

                            // start reconnect if there are 3 missed keep alives
                            if (_missedKeepAlives[endpoint] >= 3)
                            {
                                // check if a reconnection is already in progress
                                bool reconnectInProgress = false;
                                lock (_reconnectHandlersLock)
                                {
                                    foreach (SessionReconnectHandler handler in _reconnectHandlers)
                                    {
                                        if (ReferenceEquals(handler.Session, session))
                                        {
                                            reconnectInProgress = true;
                                            break;
                                        }
                                    }
                                }

                                if (!reconnectInProgress)
                                {
                                    lock (_sessionLock)
                                    {
                                        _sessions.Remove(session);
                                    }

                                    Diagnostics.Singleton.Info.NumberOfOpcSessionsConnected--;
                                    _logger.LogInformation($"RECONNECTING session {session.SessionId}...");
                                    SessionReconnectHandler reconnectHandler = new SessionReconnectHandler(_app.Telemetry);
                                    lock (_reconnectHandlersLock)
                                    {
                                        _reconnectHandlers.Add(reconnectHandler);
                                    }
                                    reconnectHandler.BeginReconnect(session, 10000, ReconnectCompleteHandler);
                                }
                            }
                        }
                        else
                        {
                            if (_missedKeepAlives.ContainsKey(endpoint) && (_missedKeepAlives[endpoint] != 0))
                            {
                                // Reset missed keep alive count
                                _logger.LogInformation("Session endpoint: {endpoint} got a keep alive after {missedKeepAlives} {verb} missed.",
                                    endpoint,
                                    _missedKeepAlives[endpoint],
                                    _missedKeepAlives[endpoint] == 1 ? "was" : "were");

                                _missedKeepAlives[endpoint] = 0;
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Exception in keep alive handling for endpoint {endpointUrl}. {message}",
                       session.ConfiguredEndpoint.EndpointUrl,
                       e.Message);
                }
            }
            else
            {
                _logger.LogWarning("Keep alive arguments invalid.");
            }
        }

        private void ReconnectCompleteHandler(object sender, EventArgs e)
        {
            // find our reconnect handler
            SessionReconnectHandler reconnectHandler = null;
            lock (_reconnectHandlersLock)
            {
                foreach (SessionReconnectHandler handler in _reconnectHandlers)
                {
                    if (ReferenceEquals(sender, handler))
                    {
                        reconnectHandler = handler;
                        break;
                    }
                }
            }

            // ignore callbacks from discarded objects
            if (reconnectHandler == null || reconnectHandler.Session == null)
            {
                return;
            }

            // update the session
            ISession session = reconnectHandler.Session;
            lock (_sessionLock)
            {
                _sessions.Add(session);
            }

            Diagnostics.Singleton.Info.NumberOfOpcSessionsConnected++;
            lock (_reconnectHandlersLock)
            {
                _reconnectHandlers.Remove(reconnectHandler);
            }
            reconnectHandler.Dispose();

            _logger.LogInformation($"RECONNECTED session {session.SessionId}!");
        }

        public async Task<string> ReadNode(string endpointUrl, string username, string password, string nodeId)
        {
            // find or create the session we need to monitor the node
            ISession session = await ConnectSessionAsync(
                endpointUrl,
                username,
                password
            ).ConfigureAwait(false);

            if (session == null)
            {
                // couldn't create the session
                throw new Exception($"Could not create session for endpoint {endpointUrl}!");
            }

            ReadValueIdCollection nodesToRead = new ReadValueIdCollection();

            ReadValueId valueId = new()
            {
                NodeId = new NodeId(nodeId),
                AttributeId = Attributes.Value,
                IndexRange = null,
                DataEncoding = null
            };
            nodesToRead.Add(valueId);

            // handle complex types
            VariableNode node = (VariableNode)await session.ReadNodeAsync(ExpandedNodeId.ToNodeId(nodeId, session.NamespaceUris)).ConfigureAwait(false);
            ComplexTypeSystem complexTypeSystem = new(session);
            ExpandedNodeId nodeTypeId = node.DataType;
            await complexTypeSystem.LoadTypeAsync(nodeTypeId).ConfigureAwait(false);

            ReadResponse response = await session.ReadAsync(null, 0, TimestampsToReturn.Both, nodesToRead, CancellationToken.None).ConfigureAwait(false);

            ClientBase.ValidateResponse(response.Results, nodesToRead);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, nodesToRead);

            if (response.Results.Count > 0 && response.Results[0].Value != null)
            {
                return response.Results[0].WrappedValue.ToString();
            }

            return string.Empty;
        }

        public async Task<string> PublishNodeAsync(NodePublishingModel nodeToPublish, CancellationToken cancellationToken = default)
        {
            // find or create the session we need to monitor the node
            ISession session = await ConnectSessionAsync(
                nodeToPublish.EndpointUrl,
                nodeToPublish.Username,
                nodeToPublish.Password
            ).ConfigureAwait(false);

            if (session == null)
            {
                // couldn't create the session
                throw new Exception($"Could not create session for endpoint {nodeToPublish.EndpointUrl}!");
            }

            Subscription opcSubscription = null;
            try
            {
                // check if there is already a subscription with the same publishing interval, which can be used to monitor the node
                int opcPublishingIntervalForNode = (nodeToPublish.OpcPublishingInterval == 0) ? (int)Settings.Instance.DefaultOpcPublishingInterval : nodeToPublish.OpcPublishingInterval;
                foreach (Subscription subscription in session.Subscriptions)
                {
                    if (subscription.PublishingInterval == opcPublishingIntervalForNode)
                    {
                        opcSubscription = subscription;
                        break;
                    }
                }

                // if there was none found, create one
                if (opcSubscription == null)
                {
                    _logger.LogInformation("PublishNode: No matching subscription with publishing interval of {publishingInterval} found, creating a new one.",
                        nodeToPublish.OpcPublishingInterval);

                    opcSubscription = await CreateSubscription(session, opcPublishingIntervalForNode).ConfigureAwait(false);
                }

                // resolve all node and namespace references in the select and where clauses
                EventFilter eventFilter = new EventFilter();
                if ((nodeToPublish.Filter != null) && (nodeToPublish.Filter.Count > 0))
                {
                    List<NodeId> ofTypes = new List<NodeId>();
                    foreach (FilterModel filter in nodeToPublish.Filter)
                    {
                        if (!string.IsNullOrEmpty(filter.OfType))
                        {
                            ofTypes.Add(ExpandedNodeId.ToNodeId(ExpandedNodeId.Parse(filter.OfType), session.NamespaceUris));
                        }
                    }

                    eventFilter.SelectClauses = await ConstructSelectClauses(session).ConfigureAwait(false);
                    eventFilter.WhereClause = ConstructWhereClause(ofTypes, EventSeverity.Min);
                }

                // if no nodeid was specified, select the server object root
                NodeId resolvedNodeId;
                if (nodeToPublish.ExpandedNodeId.Identifier == null)
                {
                    _logger.LogWarning("Selecting server root as no expanded node ID specified to publish!");
                    resolvedNodeId = ObjectIds.Server;
                }
                else
                {
                    // generate the resolved NodeId we need for publishing
                    if (nodeToPublish.ExpandedNodeId.ToString().StartsWith("nsu="))
                    {
                        resolvedNodeId = ExpandedNodeId.ToNodeId(nodeToPublish.ExpandedNodeId, session.NamespaceUris);
                    }
                    else
                    {
                        resolvedNodeId = new NodeId(nodeToPublish.ExpandedNodeId.Identifier, nodeToPublish.ExpandedNodeId.NamespaceIndex);
                    }
                }

                // if it is already published, we unpublish first, then we create a new monitored item
                foreach (MonitoredItem monitoredItem in opcSubscription.MonitoredItems)
                {
                    if (monitoredItem.ResolvedNodeId == resolvedNodeId)
                    {
                        opcSubscription.RemoveItem(monitoredItem);
                        await opcSubscription.ApplyChangesAsync().ConfigureAwait(false);
                        Diagnostics.Singleton.Info.NumberOfOpcMonitoredItemsMonitored--;
                    }
                }

                int opcSamplingIntervalForNode = (nodeToPublish.OpcSamplingInterval == 0) ? (int)Settings.Instance.DefaultOpcSamplingInterval : nodeToPublish.OpcSamplingInterval;
                MonitoredItem newMonitoredItem = new MonitoredItem(opcSubscription.DefaultItem)
                {
                    StartNodeId = resolvedNodeId,
                    AttributeId = Attributes.Value,
                    SamplingInterval = opcSamplingIntervalForNode
                };

                if (eventFilter.SelectClauses.Count > 0)
                {
                    // event
                    newMonitoredItem.Notification += _trigger.EventNotificationHandler;
                    newMonitoredItem.AttributeId = Attributes.EventNotifier;
                    newMonitoredItem.Filter = eventFilter;
                }
                else
                {
                    // data change
                    newMonitoredItem.Notification += _trigger.DataChangedNotificationHandler;
                }

                // read display name
                newMonitoredItem.DisplayName = string.Empty;
                Ua.Node node = await session.ReadNodeAsync(resolvedNodeId).ConfigureAwait(false);
                if ((node != null) && (node.DisplayName != null))
                {
                    newMonitoredItem.DisplayName = node.DisplayName.Text;
                }

                opcSubscription.AddItem(newMonitoredItem);
                await opcSubscription.ApplyChangesAsync().ConfigureAwait(false);
                Diagnostics.Singleton.Info.NumberOfOpcMonitoredItemsMonitored++;

                // create a heartbeat timer, if required
                if (nodeToPublish.HeartbeatInterval > 0)
                {
                    PeriodicPublishing heartbeat = new PeriodicPublishing(
                        (uint)nodeToPublish.HeartbeatInterval,
                        session,
                        resolvedNodeId,
                        newMonitoredItem.DisplayName,
                        _loggerFactory);

                    lock (_periodicPublishingListLock)
                    {
                        _periodicPublishingList.Add(heartbeat);
                    }
                }

                // create a skip first entry, if required
                if (nodeToPublish.SkipFirst)
                {
                    _trigger.SkipFirst[nodeToPublish.ExpandedNodeId.ToString()] = true;
                }

                _logger.LogDebug("PublishNode: Now monitoring OPC UA node {expandedNodeId} on endpoint {endpointUrl}",
                   nodeToPublish.ExpandedNodeId,
                   session.ConfiguredEndpoint.EndpointUrl);

                // update our persistency
                PersistPublishedNodes();

                return "Successfully published node " + nodeToPublish.ExpandedNodeId.ToString();
            }
            catch (ServiceResultException sre)
            {
                switch ((uint)sre.Result.StatusCode)
                {
                    case StatusCodes.BadSessionIdInvalid:
                        _logger.LogError("Session with Id {sessionId} is no longer available on endpoint {endpointUrl}. Cleaning up.",
                            session.SessionId,
                            session.ConfiguredEndpoint.EndpointUrl);
                        break;

                    case StatusCodes.BadSubscriptionIdInvalid:
                        _logger.LogError("Subscription with Id {subscription} is no longer available on endpoint {endpointUrl}. Cleaning up.",
                            opcSubscription.Id,
                            session.ConfiguredEndpoint.EndpointUrl);
                        break;

                    case StatusCodes.BadNodeIdInvalid:
                    case StatusCodes.BadNodeIdUnknown:
                        _logger.LogError("Failed to monitor node {expandedNodeId} on endpoint {endpointUrl}.",
                            nodeToPublish.ExpandedNodeId,
                            session.ConfiguredEndpoint.EndpointUrl);

                        _logger.LogError("OPC UA ServiceResultException is {result}. Please check your UA Cloud Publisher configuration for this node.", sre.Result);
                        break;

                    default:
                        _logger.LogError("Unhandled OPC UA ServiceResultException {result} when monitoring node {expandedNodeId} on endpoint {endpointUrl}. Continue.",
                            sre.Result,
                            nodeToPublish.ExpandedNodeId,
                            session.ConfiguredEndpoint.EndpointUrl);
                        break;
                }

                return sre.Message;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "PublishNode: Exception while trying to add node {expandedNodeId} for monitoring.", nodeToPublish.ExpandedNodeId);
                return e.Message;
            }
        }

        public async Task UnpublishNode(NodePublishingModel nodeToUnpublish)
        {
            // find the required session
            Session session = FindSession(nodeToUnpublish.EndpointUrl);
            if (session == null)
            {
                throw new ArgumentException($"Session for endpoint {nodeToUnpublish.EndpointUrl} no longer exists!");
            }

            // generate the resolved NodeId we need for unpublishing
            NodeId resolvedNodeId;
            if (nodeToUnpublish.ExpandedNodeId.ToString().StartsWith("nsu="))
            {
                resolvedNodeId = ExpandedNodeId.ToNodeId(nodeToUnpublish.ExpandedNodeId, session.NamespaceUris);
            }
            else
            {
                resolvedNodeId = new NodeId(nodeToUnpublish.ExpandedNodeId.Identifier, nodeToUnpublish.ExpandedNodeId.NamespaceIndex);
            }

            // loop through all subscriptions of the session
            foreach (Subscription subscription in session.Subscriptions)
            {
                // loop through all monitored items
                foreach (MonitoredItem monitoredItem in subscription.MonitoredItems)
                {
                    if (monitoredItem.ResolvedNodeId == resolvedNodeId)
                    {
                        subscription.RemoveItem(monitoredItem);
                        await subscription.ApplyChangesAsync().ConfigureAwait(false);
                        Diagnostics.Singleton.Info.NumberOfOpcMonitoredItemsMonitored--;

                        // cleanup empty subscriptions and sessions
                        if (subscription.MonitoredItemCount == 0)
                        {
                            await session.RemoveSubscriptionAsync(subscription).ConfigureAwait(false);
                            Diagnostics.Singleton.Info.NumberOfOpcSubscriptionsConnected--;
                        }

                        // update our persistency
                        PersistPublishedNodes();

                        return;
                    }
                }
                break;
            }
        }

        public IEnumerable<PublishNodesInterfaceModel> GetPublishedNodes()
        {
            List<PublishNodesInterfaceModel> publisherConfigurationFileEntries = new List<PublishNodesInterfaceModel>();

            try
            {
                // loop through all sessions
                lock (_sessionLock)
                {
                    foreach (ISession session in _sessions)
                    {
                        UserAuthModeEnum authenticationMode = UserAuthModeEnum.Anonymous;
                        string username = null;
                        string password = null;

                        if (session.Identity.TokenType == UserTokenType.UserName)
                        {
                            authenticationMode = UserAuthModeEnum.UsernamePassword;

                            UserNameIdentityToken token = (UserNameIdentityToken)session.Identity.GetIdentityToken();
                            username = token.UserName;
                            password = Encoding.UTF8.GetString(token.DecryptedPassword);
                        }

                        PublishNodesInterfaceModel publisherConfigurationFileEntry = new PublishNodesInterfaceModel
                        {
                            EndpointUrl = session.ConfiguredEndpoint.EndpointUrl.AbsoluteUri,
                            OpcAuthenticationMode = authenticationMode,
                            UserName = username,
                            Password = password,
                            OpcNodes = new List<VariableModel>(),
                            OpcEvents = new List<EventModel>()
                        };

                        foreach (Subscription subscription in session.Subscriptions)
                        {
                            foreach (MonitoredItem monitoredItem in subscription.MonitoredItems)
                            {
                                if (monitoredItem.Filter != null)
                                {
                                    // event
                                    EventModel publishedEvent = new EventModel()
                                    {
                                        ExpandedNodeId = NodeId.ToExpandedNodeId(monitoredItem.ResolvedNodeId, monitoredItem.Subscription.Session.NamespaceUris).ToString(),
                                        Filter = new List<FilterModel>()
                                    };

                                    if (monitoredItem.Filter is EventFilter)
                                    {
                                        EventFilter eventFilter = (EventFilter)monitoredItem.Filter;
                                        if (eventFilter.WhereClause != null)
                                        {
                                            foreach (ContentFilterElement whereClauseElement in eventFilter.WhereClause.Elements)
                                            {
                                                if (whereClauseElement.FilterOperator == FilterOperator.OfType)
                                                {
                                                    foreach (ExtensionObject operand in whereClauseElement.FilterOperands)
                                                    {
                                                        FilterModel filter = new FilterModel()
                                                        {
                                                            OfType = NodeId.ToExpandedNodeId(new NodeId(operand.ToString()), monitoredItem.Subscription.Session.NamespaceUris).ToString()
                                                        };

                                                        publishedEvent.Filter.Add(filter);
                                                    }
                                                }
                                            }
                                        }
                                    }

                                    publisherConfigurationFileEntry.OpcEvents.Add(publishedEvent);
                                }
                                else
                                {
                                    // variable
                                    VariableModel publishedVariable = new VariableModel()
                                    {
                                        Id = NodeId.ToExpandedNodeId(monitoredItem.ResolvedNodeId, monitoredItem.Subscription.Session.NamespaceUris).ToString(),
                                        OpcSamplingInterval = monitoredItem.SamplingInterval,
                                        OpcPublishingInterval = subscription.PublishingInterval,
                                        HeartbeatInterval = 0,
                                        SkipFirst = false
                                    };

                                    lock (_periodicPublishingListLock)
                                    {
                                        foreach (PeriodicPublishing heartbeat in _periodicPublishingList)
                                        {
                                            if ((heartbeat.HeartBeatSession == session) && (heartbeat.HeartBeatNodeId == monitoredItem.ResolvedNodeId))
                                            {
                                                publishedVariable.HeartbeatInterval = (int)heartbeat.HeartBeatInterval;
                                                break;
                                            }
                                        }
                                    }

                                    ExpandedNodeId expandedNode = new ExpandedNodeId(monitoredItem.ResolvedNodeId);
                                    if (_trigger.SkipFirst.ContainsKey(expandedNode.ToString()))
                                    {
                                        publishedVariable.SkipFirst = true;
                                    }

                                    publisherConfigurationFileEntry.OpcNodes.Add(publishedVariable);
                                }
                            }
                        }

                        if ((publisherConfigurationFileEntry.OpcEvents.Count > 0) || (publisherConfigurationFileEntry.OpcNodes.Count > 0))
                        {
                            publisherConfigurationFileEntries.Add(publisherConfigurationFileEntry);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Reading configuration file entries failed.");
                return null;
            }

            return publisherConfigurationFileEntries;
        }

        private void PersistPublishedNodes(CancellationToken cancellationToken = default)
        {
            try
            {
                // iterate through all sessions, subscriptions and monitored items and create config file entries
                IEnumerable<PublishNodesInterfaceModel> publisherNodeConfiguration = GetPublishedNodes();

                // update the persistency file
                File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), "settings", "persistency.json"), Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(publisherNodeConfiguration, Formatting.Indented)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Update of persistency file failed.");
            }
        }

        public async Task<ReferenceDescriptionCollection> Browse(string endpointUrl, string username, string password, BrowseDescription nodeToBrowse, bool throwOnError)
        {
            // find or create the session
            ISession session = await ConnectSessionAsync(
                endpointUrl,
                username,
                password
            ).ConfigureAwait(false);

            if (session == null)
            {
                // couldn't create the session
                throw new Exception($"Could not create session for endpoint {endpointUrl}!");
            }

            return await Browse(session, nodeToBrowse, throwOnError).ConfigureAwait(false);
        }

        private static async Task<ReferenceDescriptionCollection> Browse(ISession session, BrowseDescription nodeToBrowse, bool throwOnError)
        {
            try
            {
                // start the browse operation.
                (ResponseHeader responseHeader,
                Byte[] continuationPoints,
                ReferenceDescriptionCollection referencesList) = await session.BrowseAsync(
                    null,
                    null,
                    nodeToBrowse.NodeId,
                    0,
                    BrowseDirection.Forward,
                    null,
                    true,
                    nodeToBrowse.NodeClassMask
                    ).ConfigureAwait(false);

                do
                {
                    // check if all references have been fetched.
                    if (referencesList.Count == 0 || continuationPoints == null)
                    {
                        break;
                    }

                    // continue browse operation.
                    (ResponseHeader responseHeaderNext,
                    Byte[] continuationPointsNext,
                    ReferenceDescriptionCollection referencesListNext) = await session.BrowseNextAsync(
                        null,
                        false,
                        continuationPoints
                    ).ConfigureAwait(false);

                    if (referencesListNext.Count > 0)
                    {
                        // append results to the master list
                        referencesList.AddRange(referencesListNext);
                    }

                    if (continuationPointsNext == null)
                    {
                        break;
                    }
                    else
                    {
                        continuationPoints = continuationPointsNext;
                    }
                }
                while (true);

                // return complete list.
                return referencesList;
            }
            catch (Exception exception)
            {
                if (throwOnError)
                {
                    throw new ServiceResultException(exception, StatusCodes.BadUnexpectedError);
                }

                return null;
            }
        }

        public async Task<List<UANodeInformation>> BrowseVariableNodesResursivelyAsync(string endpointUrl, string username, string password, NodeId nodeId)
        {
            List<UANodeInformation> results = new();

            if (nodeId == null)
            {
                nodeId = ObjectIds.ObjectsFolder;
            }

            BrowseDescription nodeToBrowse = new BrowseDescription
            {
                NodeId = nodeId,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable),
                ResultMask = (uint)BrowseResultMask.All
            };

            ReferenceDescriptionCollection references = await Browse(endpointUrl, username, password, nodeToBrowse, true).ConfigureAwait(false);

            List<string> processedReferences = new();
            foreach (ReferenceDescription nodeReference in references)
            {
                UANodeInformation nodeInfo = new()
                {
                    DisplayName = nodeReference.DisplayName.Text,
                    Type = nodeReference.NodeClass.ToString()
                };

                try
                {
                    // find or create the session
                    ISession session = await ConnectSessionAsync(
                        endpointUrl,
                        username,
                        password
                    ).ConfigureAwait(false);

                    if (session == null)
                    {
                        // couldn't create the session
                        throw new Exception($"Could not create session for endpoint {endpointUrl}!");
                    }

                    nodeInfo.ApplicationUri = session.ServerUris.ToArray()[0];
                    nodeInfo.Endpoint = session.Endpoint.EndpointUrl;

                    if (nodeId.NamespaceIndex == 0)
                    {
                        nodeInfo.Parent = "nsu=http://opcfoundation.org/UA;" + nodeId.ToString();
                    }
                    else
                    {
                        nodeInfo.Parent = NodeId.ToExpandedNodeId(ExpandedNodeId.ToNodeId(nodeId, session.NamespaceUris), session.NamespaceUris).ToString();
                    }

                    if (nodeReference.NodeId.NamespaceIndex == 0)
                    {
                        nodeInfo.ExpandedNodeId = "nsu=http://opcfoundation.org/UA;" + nodeReference.NodeId.ToString();
                    }
                    else
                    {
                        nodeInfo.ExpandedNodeId = NodeId.ToExpandedNodeId(ExpandedNodeId.ToNodeId(nodeReference.NodeId, session.NamespaceUris), session.NamespaceUris).ToString();
                    }

                    if (nodeReference.NodeClass == NodeClass.Variable)
                    {
                        try
                        {
                            DataValue value = await session.ReadValueAsync(ExpandedNodeId.ToNodeId(nodeReference.NodeId, session.NamespaceUris)).ConfigureAwait(false);
                            if ((value != null) && (value.WrappedValue != Variant.Null))
                            {
                                nodeInfo.VariableCurrentValue = value.ToString();
                                nodeInfo.VariableType = value.WrappedValue.TypeInfo.ToString();
                            }
                        }
                        catch (Exception)
                        {
                            // do nothing
                        }
                    }

                    List<UANodeInformation> childReferences = await BrowseVariableNodesResursivelyAsync(endpointUrl, username, password, ExpandedNodeId.ToNodeId(nodeReference.NodeId, session.NamespaceUris)).ConfigureAwait(false);

                    nodeInfo.References = new string[childReferences.Count];
                    for (int i = 0; i < childReferences.Count; i++)
                    {
                        nodeInfo.References[i] = childReferences[i].ExpandedNodeId.ToString();
                    }

                    results.AddRange(childReferences);
                }
                catch (Exception)
                {
                    // skip this node
                    continue;
                }

                processedReferences.Add(nodeReference.NodeId.ToString());
                results.Add(nodeInfo);
            }

            return results;
        }

        public async Task GDSServerPush(string endpointURL, string adminUsername, string adminPassword)
        {
            try
            {
                ServerPushConfigurationClient serverPushClient = new(_app.UAApplicationInstance.ApplicationConfiguration);

                // use environment variables if nothing else was specified
                if (string.IsNullOrEmpty(adminUsername))
                {
                    adminUsername = Environment.GetEnvironmentVariable("OPCUA_USERNAME");
                }

                if (string.IsNullOrEmpty(adminPassword))
                {
                    adminPassword = Environment.GetEnvironmentVariable("OPCUA_PASSWORD");
                }

                serverPushClient.AdminCredentials = new UserIdentity(adminUsername, Encoding.UTF8.GetBytes(adminPassword));

                await serverPushClient.ConnectAsync(endpointURL).ConfigureAwait(false);

                byte[] unusedNonce = Array.Empty<byte>();
                byte[] certificateRequest = await serverPushClient.CreateSigningRequestAsync(
                    NodeId.Null,
                    serverPushClient.ApplicationCertificateType,
                    string.Empty,
                    false,
                    unusedNonce).ConfigureAwait(false);

                X509Certificate2 certificate = ProcessSigningRequest(
                    serverPushClient.Session.ServerUris.ToArray()[0],
                    null,
                    certificateRequest);

                byte[][] issuerCertificates = [_app.IssuerCert.Export(X509ContentType.Cert)];
                await serverPushClient.UpdateCertificateAsync(
                    NodeId.Null,
                    serverPushClient.ApplicationCertificateType,
                    certificate.Export(X509ContentType.Cert),
                    string.Empty,
                    Array.Empty<byte>(),
                    issuerCertificates).ConfigureAwait(false);

                // store in our own trust list
                await _app.UAApplicationInstance.AddOwnCertificateToTrustedStoreAsync(certificate, CancellationToken.None).ConfigureAwait(false);

                // update trust list on server
                TrustListDataType trustList = await GetTrustLists().ConfigureAwait(false);

                await serverPushClient.UpdateTrustListAsync(trustList).ConfigureAwait(false);

                await serverPushClient.ApplyChangesAsync().ConfigureAwait(false);

                await serverPushClient.DisconnectAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GDS server push failed with: " + ex.Message);
                throw;
            }
        }

        private X509Certificate2 ProcessSigningRequest(string applicationUri, string[] domainNames, byte[] certificateRequest)
        {
            try
            {
                var pkcs10CertificationRequest = new Org.BouncyCastle.Pkcs.Pkcs10CertificationRequest(certificateRequest);

                if (!pkcs10CertificationRequest.Verify())
                {
                    throw new ServiceResultException(Ua.StatusCodes.BadInvalidArgument, "CSR signature invalid.");
                }

                var info = pkcs10CertificationRequest.GetCertificationRequestInfo();
                var altNameExtension = GetAltNameExtensionFromCSRInfo(info);
                if (altNameExtension != null)
                {
                    if (altNameExtension.Uris.Count > 0)
                    {
                        if (!altNameExtension.Uris.Contains(applicationUri))
                        {
                            var applicationUriMissing = new StringBuilder();
                            applicationUriMissing.AppendLine("Expected AltNameExtension (ApplicationUri):");
                            applicationUriMissing.AppendLine(applicationUri);
                            applicationUriMissing.AppendLine("CSR AltNameExtensions found:");
                            foreach (string uri in altNameExtension.Uris)
                            {
                                applicationUriMissing.AppendLine(uri);
                            }
                            throw new ServiceResultException(Ua.StatusCodes.BadCertificateUriInvalid,
                                applicationUriMissing.ToString());
                        }
                    }

                    if (altNameExtension.IPAddresses.Count > 0 || altNameExtension.DomainNames.Count > 0)
                    {
                        var domainNameList = new List<string>();
                        domainNameList.AddRange(altNameExtension.DomainNames);
                        domainNameList.AddRange(altNameExtension.IPAddresses);
                        domainNames = domainNameList.ToArray();
                    }
                }

                return CertificateBuilder.Create(new X500DistinguishedName(info.Subject.GetEncoded()))
                    .AddExtension(new X509SubjectAltNameExtension(applicationUri, domainNames))
                    .SetNotBefore(DateTime.Today.AddDays(-1))
                    .SetLifeTime(12)
                    .SetHashAlgorithm(X509Utils.GetRSAHashAlgorithmName(2048))
                    .SetIssuer(_app.IssuerCert)
                    .SetRSAPublicKey(info.SubjectPublicKeyInfo.GetEncoded())
                    .CreateForRSA();
            }
            catch (Exception ex)
            {
                if (ex is ServiceResultException)
                {
                    throw;
                }
                throw new ServiceResultException(Ua.StatusCodes.BadInvalidArgument, ex.Message);
            }
        }

        private X509SubjectAltNameExtension GetAltNameExtensionFromCSRInfo(Org.BouncyCastle.Asn1.Pkcs.CertificationRequestInfo info)
        {
            try
            {
                for (int i = 0; i < info.Attributes.Count; i++)
                {
                    var sequence = Org.BouncyCastle.Asn1.Asn1Sequence.GetInstance(info.Attributes[i].ToAsn1Object());
                    var oid = Org.BouncyCastle.Asn1.DerObjectIdentifier.GetInstance(sequence[0].ToAsn1Object());

                    if (oid.Equals(Org.BouncyCastle.Asn1.Pkcs.PkcsObjectIdentifiers.Pkcs9AtExtensionRequest))
                    {
                        var extensionInstance = Org.BouncyCastle.Asn1.DerSet.GetInstance(sequence[1]);
                        var extensionSequence = Org.BouncyCastle.Asn1.Asn1Sequence.GetInstance(extensionInstance[0]);
                        var extensions = Org.BouncyCastle.Asn1.X509.X509Extensions.GetInstance(extensionSequence);
                        Org.BouncyCastle.Asn1.X509.X509Extension extension = extensions.GetExtension(Org.BouncyCastle.Asn1.X509.X509Extensions.SubjectAlternativeName);
                        var asnEncodedAltNameExtension = new System.Security.Cryptography.AsnEncodedData(Org.BouncyCastle.Asn1.X509.X509Extensions.SubjectAlternativeName.ToString(), extension.Value.GetOctets());
                        var altNameExtension = new X509SubjectAltNameExtension(asnEncodedAltNameExtension, extension.IsCritical);
                        return altNameExtension;
                    }
                }
            }
            catch (Exception)
            {
                throw new ServiceResultException(Ua.StatusCodes.BadInvalidArgument, "CSR altNameExtension invalid.");
            }
            return null;
        }

        private async Task<TrustListDataType> GetTrustLists()
        {
            ByteStringCollection trusted = new ByteStringCollection();
            ByteStringCollection trustedCrls = new ByteStringCollection();
            ByteStringCollection issuers = new ByteStringCollection();
            ByteStringCollection issuersCrls = new ByteStringCollection();

            CertificateTrustList ownTrustList = _app.UAApplicationInstance.ApplicationConfiguration.SecurityConfiguration.TrustedPeerCertificates;
            foreach (X509Certificate2 cert in await ownTrustList.GetCertificatesAsync(_app.Telemetry).ConfigureAwait(false))
            {
                trusted.Add(cert.Export(X509ContentType.Cert));
            }

            issuers.Add(_app.IssuerCert.Export(X509ContentType.Cert));

            TrustListDataType trustList = new TrustListDataType()
            {
                SpecifiedLists = (uint)(TrustListMasks.All),
                TrustedCertificates = trusted,
                TrustedCrls = trustedCrls,
                IssuerCertificates = issuers,
                IssuerCrls = issuersCrls
            };

            return trustList;
        }

        public async Task WoTConUpload(string endpoint, string username, string password, byte[] bytes, string assetName)
        {
            ISession session = null;
            NodeId fileId = null;
            object fileHandle = null;
            try
            {
                session = await ConnectSessionAsync(endpoint, username, password).ConfigureAwait(false);
                if (session == null)
                {
                    // couldn't create the session
                    throw new Exception($"Could not create session for endpoint {endpoint}!");
                }

                NodeId createNodeId = new(WoTAssetConnectionManagement_CreateAsset, (ushort)session.NamespaceUris.GetIndex("http://opcfoundation.org/UA/WoT-Con/"));
                NodeId deleteNodeId = new(WoTAssetConnectionManagement_DeleteAsset, (ushort)session.NamespaceUris.GetIndex("http://opcfoundation.org/UA/WoT-Con/"));
                NodeId parentNodeId = new(WoTAssetConnectionManagement, (ushort)session.NamespaceUris.GetIndex("http://opcfoundation.org/UA/WoT-Con/"));

                Variant assetId = new(string.Empty);

                try
                {
                    assetId = await ExecuteCommand(session, createNodeId, parentNodeId, assetName, null).ConfigureAwait(false);
                }
                catch (ServiceResultException ex)
                {
                    if (ex.StatusCode == StatusCodes.BadBrowseNameDuplicated)
                    {
                        // delete existing asset first
                        assetId = await ExecuteCommand(session, deleteNodeId, parentNodeId, new NodeId(ex.Result.LocalizedText?.Text), null).ConfigureAwait(false);

                        // now try again
                        assetId = await ExecuteCommand(session, createNodeId, parentNodeId, assetName, null).ConfigureAwait(false);
                    }
                    else
                    {
                        throw;
                    }
                }

                BrowseDescription nodeToBrowse = new()
                {
                    NodeId = (NodeId)assetId.Value,
                    BrowseDirection = BrowseDirection.Forward,
                    NodeClassMask = (uint)NodeClass.Object,
                    ResultMask = (uint)BrowseResultMask.All
                };

                ReferenceDescriptionCollection references = await Browse(endpoint, username, password, nodeToBrowse, true).ConfigureAwait(false);

                fileId = (NodeId)references[0].NodeId;
                fileHandle = await ExecuteCommand(session, MethodIds.FileType_Open, fileId, (byte)6, null).ConfigureAwait(false);

                for (int i = 0; i < bytes.Length; i += 3000)
                {
                    byte[] chunk = bytes.AsSpan(i, Math.Min(3000, bytes.Length - i)).ToArray();

                    await ExecuteCommand(session, MethodIds.FileType_Write, fileId, fileHandle, chunk).ConfigureAwait(false);
                }

                await ExecuteCommand(session, MethodIds.FileType_Close, fileId, fileHandle, null).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload WoT Thing Description");

                if ((session != null) && (fileId != null) && (fileHandle != null))
                {
                    await ExecuteCommand(session, MethodIds.FileType_Close, fileId, fileHandle, null).ConfigureAwait(false);
                }

                throw;
            }
            finally
            {
                if (session != null)
                {
                    if (session.Connected)
                    {
                        await session.CloseAsync().ConfigureAwait(false);
                    }

                    session.Dispose();
                }
            }
        }

        public async Task UANodesetUpload(string endpoint, string username, string password, byte[] bytes)
        {
            ISession session = null;
            NodeId methodNodeId = null;
            object fileHandle = null;
            try
            {
                session = await ConnectSessionAsync(endpoint, username, password).ConfigureAwait(false);
                if (session == null)
                {
                    // couldn't create the session
                    throw new Exception($"Could not create session for endpoint {endpoint}!");
                }

                methodNodeId = new("NodesetFileUpload", (ushort)session.NamespaceUris.GetIndex("http://opcfoundation.org/UA/WoT-Con/"));

                fileHandle = await ExecuteCommand(session, MethodIds.FileType_Open, methodNodeId, (byte)6, null).ConfigureAwait(false);

                for (int i = 0; i < bytes.Length; i += 3000)
                {
                    byte[] chunk = bytes.AsSpan(i, Math.Min(3000, bytes.Length - i)).ToArray();

                    await ExecuteCommand(session, MethodIds.FileType_Write, methodNodeId, fileHandle, chunk).ConfigureAwait(false);
                }

                await ExecuteCommand(session, MethodIds.FileType_Close, methodNodeId, fileHandle, null).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload WoT Thing Description");

                if ((session != null) && (methodNodeId != null) && (fileHandle != null))
                {
                    await ExecuteCommand(session, MethodIds.FileType_Close, methodNodeId, fileHandle, null).ConfigureAwait(false);
                }

                throw;
            }
            finally
            {
                if (session != null)
                {
                    if (session.Connected)
                    {
                        await session.CloseAsync().ConfigureAwait(false);
                    }

                    session.Dispose();
                }
            }
        }

        private async Task<Variant> ExecuteCommand(ISession session, NodeId nodeId, NodeId parentNodeId, object argument1, object argument2)
        {
            try
            {
                List<object> arguments = new();
                if (argument1 != null)
                {
                    arguments.Add(argument1);
                }
                if (argument2 != null)
                {
                    arguments.Add(argument2);
                }

                IList<object> results = await session.CallAsync(parentNodeId, nodeId, CancellationToken.None, arguments.ToArray()).ConfigureAwait(false);

                if ((results != null) && (results.Count > 0))
                {
                    return new Variant(results[0]);
                }

                return new Variant(string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Executing OPC UA command failed!");
                throw;
            }
        }

        private async Task<SimpleAttributeOperandCollection> ConstructSelectClauses(ISession session)
        {
            // browse the type model in the server address space to find the fields available for the event type.
            SimpleAttributeOperandCollection selectClauses = new SimpleAttributeOperandCollection();

            // must always request the NodeId for the condition instances.
            // this can be done by specifying an operand with an empty browse path.
            SimpleAttributeOperand operand = new SimpleAttributeOperand();

            operand.TypeDefinitionId = ObjectTypeIds.BaseEventType;
            operand.AttributeId = Attributes.NodeId;
            operand.BrowsePath = new QualifiedNameCollection();

            selectClauses.Add(operand);

            // add the fields for the selected EventTypes.
            await CollectFields(session, ObjectTypeIds.BaseEventType, selectClauses).ConfigureAwait(false);

            return selectClauses;
        }

        private ContentFilter ConstructWhereClause(IList<NodeId> eventTypes, EventSeverity severity)
        {
            ContentFilter whereClause = new ContentFilter();

            // the code below constructs a filter that looks like this:
            // (Severity >= X OR LastSeverity >= X) AND (SuppressedOrShelved == False) AND (OfType(A) OR OfType(B))

            // add the severity.
            ContentFilterElement element1 = null;
            if (severity > EventSeverity.Min)
            {
                // select the Severity property of the event.
                SimpleAttributeOperand operand1 = new SimpleAttributeOperand();
                operand1.TypeDefinitionId = ObjectTypeIds.BaseEventType;
                operand1.BrowsePath.Add(BrowseNames.Severity);
                operand1.AttributeId = Attributes.Value;

                // specify the value to compare the Severity property with.
                LiteralOperand operand2 = new LiteralOperand();
                operand2.Value = new Variant((ushort)severity);

                // specify that the Severity property must be GreaterThanOrEqual the value specified.
                element1 = whereClause.Push(FilterOperator.GreaterThanOrEqual, operand1, operand2);
            }

            // add the event types.
            ContentFilterElement element2 = null;
            if (eventTypes != null && eventTypes.Count > 0)
            {
                // save the last element.
                for (int i = 0; i < eventTypes.Count; i++)
                {
                    // we uses the 'OfType' operator to limit events to thoses with specified event type.
                    LiteralOperand operand1 = new LiteralOperand();
                    operand1.Value = new Variant(eventTypes[i]);
                    ContentFilterElement element3 = whereClause.Push(FilterOperator.OfType, operand1);

                    // need to chain multiple types together with an OR clause.
                    if (element2 != null)
                    {
                        element2 = whereClause.Push(FilterOperator.Or, element2, element3);
                    }
                    else
                    {
                        element2 = element3;
                    }
                }

                // need to link the set of event types with the previous filters.
                if (element1 != null)
                {
                    whereClause.Push(FilterOperator.And, element1, element2);
                }
            }

            return whereClause;
        }

        private async Task CollectFields(ISession session, NodeId eventTypeId, SimpleAttributeOperandCollection eventFields)
        {
            // get the supertypes.
            ReferenceDescriptionCollection supertypes = await BrowseSuperTypes(session, eventTypeId, false).ConfigureAwait(false);

            if (supertypes == null)
            {
                return;
            }

            // process the types starting from the top of the tree.
            Dictionary<NodeId, QualifiedNameCollection> foundNodes = new Dictionary<NodeId, QualifiedNameCollection>();
            QualifiedNameCollection parentPath = new QualifiedNameCollection();

            for (int i = supertypes.Count - 1; i >= 0; i--)
            {
                await CollectFields(session, (NodeId)supertypes[i].NodeId, parentPath, eventFields, foundNodes).ConfigureAwait(false);
            }

            // collect the fields for the selected type.
            await CollectFields(session, eventTypeId, parentPath, eventFields, foundNodes).ConfigureAwait(false);
        }

        private async Task CollectFields(
            ISession session,
            NodeId nodeId,
            QualifiedNameCollection parentPath,
            SimpleAttributeOperandCollection eventFields,
            Dictionary<NodeId, QualifiedNameCollection> foundNodes)
        {
            // find all of the children of the field.
            BrowseDescription nodeToBrowse = new BrowseDescription();

            nodeToBrowse.NodeId = nodeId;
            nodeToBrowse.BrowseDirection = BrowseDirection.Forward;
            nodeToBrowse.ReferenceTypeId = ReferenceTypeIds.Aggregates;
            nodeToBrowse.IncludeSubtypes = true;
            nodeToBrowse.NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable);
            nodeToBrowse.ResultMask = (uint)BrowseResultMask.All;

            ReferenceDescriptionCollection children = await Browse(session, nodeToBrowse, false).ConfigureAwait(false);

            if (children == null)
            {
                return;
            }

            // process the children.
            for (int i = 0; i < children.Count; i++)
            {
                ReferenceDescription child = children[i];

                if (child.NodeId.IsAbsolute)
                {
                    continue;
                }

                // construct browse path.
                QualifiedNameCollection browsePath = new(parentPath) {
                    child.BrowseName
                };

                // check if the browse path is already in the list.
                if (!ContainsPath(eventFields, browsePath))
                {
                    SimpleAttributeOperand field = new SimpleAttributeOperand();

                    field.TypeDefinitionId = ObjectTypeIds.BaseEventType;
                    field.BrowsePath = browsePath;
                    field.AttributeId = (child.NodeClass == NodeClass.Variable) ? Attributes.Value : Attributes.NodeId;

                    eventFields.Add(field);
                }

                // recusively find all of the children.
                NodeId targetId = (NodeId)child.NodeId;

                // need to guard against loops.
                if (!foundNodes.ContainsKey(targetId))
                {
                    foundNodes.Add(targetId, browsePath);
                    await CollectFields(session, (NodeId)child.NodeId, browsePath, eventFields, foundNodes).ConfigureAwait(false);
                }
            }
        }

        private bool ContainsPath(SimpleAttributeOperandCollection selectClause, QualifiedNameCollection browsePath)
        {
            for (int i = 0; i < selectClause.Count; i++)
            {
                SimpleAttributeOperand field = selectClause[i];

                if (field.BrowsePath.Count != browsePath.Count)
                {
                    continue;
                }

                bool match = true;

                for (int j = 0; j < field.BrowsePath.Count; j++)
                {
                    if (field.BrowsePath[j] != browsePath[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return true;
                }
            }

            return false;
        }

        public static async Task<ReferenceDescriptionCollection> BrowseSuperTypes(ISession session, NodeId typeId, bool throwOnError)
        {
            ReferenceDescriptionCollection supertypes = new ReferenceDescriptionCollection();

            try
            {
                // find all of the children of the field.
                BrowseDescription nodeToBrowse = new BrowseDescription();

                nodeToBrowse.NodeId = typeId;
                nodeToBrowse.BrowseDirection = BrowseDirection.Inverse;
                nodeToBrowse.ReferenceTypeId = ReferenceTypeIds.HasSubtype;
                nodeToBrowse.IncludeSubtypes = false; // more efficient to use IncludeSubtypes=False when possible.
                nodeToBrowse.NodeClassMask = 0; // the HasSubtype reference already restricts the targets to Types.
                nodeToBrowse.ResultMask = (uint)BrowseResultMask.All;

                ReferenceDescriptionCollection references = await Browse(session, nodeToBrowse, throwOnError).ConfigureAwait(false);

                while (references != null && references.Count > 0)
                {
                    // should never be more than one supertype.
                    supertypes.Add(references[0]);

                    // only follow references within this server.
                    if (references[0].NodeId.IsAbsolute)
                    {
                        break;
                    }

                    // get the references for the next level up.
                    nodeToBrowse.NodeId = (NodeId)references[0].NodeId;
                    references = await Browse(session, nodeToBrowse, throwOnError).ConfigureAwait(false);
                }

                // return complete list.
                return supertypes;
            }
            catch (Exception exception)
            {
                if (throwOnError)
                {
                    throw new ServiceResultException(exception, StatusCodes.BadUnexpectedError);
                }

                return null;
            }
        }
    }
}
