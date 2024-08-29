
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
        private const uint WoTAssetFileType_CloseAndUpdate = 111;

        private readonly IUAApplication _app;
        private readonly ILogger _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IFileStorage _storage;

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
            IMessageSource trigger,
            IFileStorage storage)
        {
            _logger = loggerFactory.CreateLogger("UAClient");
            _loggerFactory = loggerFactory;
            _app = app;
            _trigger = trigger;
            _storage = storage;
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
                        selectedEndpoint = CoreClientUtils.SelectEndpoint(endpointUrl, true);

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

        private async Task<Session> ConnectSessionAsync(string endpointUrl, string username, string password)
        {
            // check if the required session is already available
            Session existingSession = FindSession(endpointUrl);
            if (existingSession != null)
            {
                return existingSession;
            }

            EndpointDescription selectedEndpoint = null;
            ITransportWaitingConnection connection = null;
            if (Settings.Instance.UseReverseConnect)
            {
                _logger.LogInformation("Waiting for reverse connection from {0}", endpointUrl);
                connection = await _app.ReverseConnectManager.WaitForConnection(new Uri(endpointUrl), null, new CancellationTokenSource(30_000).Token).ConfigureAwait(false);
                if (connection == null)
                {
                    throw new ServiceResultException(StatusCodes.BadTimeout, "Waiting for a reverse connection timed out after 30 seconds.");
                }

                selectedEndpoint = CoreClientUtils.SelectEndpoint(_app.UAApplicationInstance.ApplicationConfiguration, connection, true);
            }
            else
            {
                selectedEndpoint = CoreClientUtils.SelectEndpoint(endpointUrl, true);
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
                userIdentity = new UserIdentity(username, password);
            }

            Session newSession = null;
            try
            {
                newSession = await Session.Create(
                    _app.UAApplicationInstance.ApplicationConfiguration,
                    configuredEndpoint,
                    true,
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

                await _complexTypeList[newSession].Load().ConfigureAwait(false);
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
                            subscription.ApplyChanges();
                        }
                        Diagnostics.Singleton.Info.NumberOfOpcMonitoredItemsMonitored -= (int)subscription.MonitoredItemCount;

                        session.RemoveSubscription(subscription);
                        Diagnostics.Singleton.Info.NumberOfOpcSubscriptionsConnected--;
                    }

                    string endpoint = session.ConfiguredEndpoint.EndpointUrl.AbsoluteUri;
                    session.Close();
                    _sessions.Remove(session);
                    _complexTypeList.Remove(session);
                    Diagnostics.Singleton.Info.NumberOfOpcSessionsConnected--;

                    _logger.LogInformation("Session to endpoint {endpoint} closed successfully.", endpoint);
                }
            }

            // update our persistency
            if (updatePersistencyFile)
            {
                PersistPublishedNodesAsync().GetAwaiter().GetResult();
            }
        }

        private Subscription CreateSubscription(Session session, ref int publishingInterval)
        {
            Subscription subscription = new Subscription(session.DefaultSubscription) {
                PublishingInterval = publishingInterval,
            };

            // add needs to happen before create to set the Session property
            session.AddSubscription(subscription);
            subscription.Create();

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
                                    SessionReconnectHandler reconnectHandler = new SessionReconnectHandler();
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

        public string ReadNode(string endpointUrl, string username, string password, ref string nodeId)
        {
            // find or create the session we need to monitor the node
            Session session = ConnectSessionAsync(
                endpointUrl,
                username,
                password
            ).GetAwaiter().GetResult();

            if (session == null)
            {
                // couldn't create the session
                throw new Exception($"Could not create session for endpoint {endpointUrl}!");
            }

            DataValueCollection values = null;
            DiagnosticInfoCollection diagnosticInfos = null;
            ReadValueIdCollection nodesToRead = new ReadValueIdCollection();

            ReadValueId valueId = new()
            {
                NodeId = new NodeId(nodeId),
                AttributeId = Attributes.Value,
                IndexRange = null,
                DataEncoding = null
            };
            nodesToRead.Add(valueId);

            session.Read(null, 0, TimestampsToReturn.Both, nodesToRead, out values, out diagnosticInfos);
            if (values.Count > 0 && values[0].Value != null)
            {
                nodeId = new ExpandedNodeId(valueId.NodeId, session.NamespaceUris.ToArray()[valueId.NodeId.NamespaceIndex]).ToString();
                return values[0].WrappedValue.ToString();
            }

            return string.Empty;
        }

        public async Task<string> PublishNodeAsync(NodePublishingModel nodeToPublish, CancellationToken cancellationToken = default)
        {
            // find or create the session we need to monitor the node
            Session session = await ConnectSessionAsync(
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

                    opcSubscription = CreateSubscription(session, ref opcPublishingIntervalForNode);
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

                    eventFilter.SelectClauses = ConstructSelectClauses(session);
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
                        opcSubscription.ApplyChanges();
                    }
                }

                int opcSamplingIntervalForNode = (nodeToPublish.OpcSamplingInterval == 0) ? (int)Settings.Instance.DefaultOpcSamplingInterval : nodeToPublish.OpcSamplingInterval;
                MonitoredItem newMonitoredItem = new MonitoredItem(opcSubscription.DefaultItem) {
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
                Ua.Node node = session.ReadNode(resolvedNodeId);
                if ((node != null) && (node.DisplayName != null))
                {
                    newMonitoredItem.DisplayName = node.DisplayName.Text;
                }

                opcSubscription.AddItem(newMonitoredItem);
                opcSubscription.ApplyChanges();

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

                Diagnostics.Singleton.Info.NumberOfOpcMonitoredItemsMonitored++;

                // update our persistency
                PersistPublishedNodesAsync().GetAwaiter().GetResult();

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

        public void UnpublishNode(NodePublishingModel nodeToUnpublish)
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
                        subscription.ApplyChanges();

                        Diagnostics.Singleton.Info.NumberOfOpcMonitoredItemsMonitored--;

                        // cleanup empty subscriptions and sessions
                        if (subscription.MonitoredItemCount == 0)
                        {
                            session.RemoveSubscription(subscription);
                            Diagnostics.Singleton.Info.NumberOfOpcSubscriptionsConnected--;
                        }

                        // update our persistency
                        PersistPublishedNodesAsync().GetAwaiter().GetResult();

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
                            password = token.DecryptedPassword;
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

        private async Task PersistPublishedNodesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // iterate through all sessions, subscriptions and monitored items and create config file entries
                IEnumerable<PublishNodesInterfaceModel> publisherNodeConfiguration = GetPublishedNodes();

                // update the persistency file
                if (await _storage.StoreFileAsync(Path.Combine(Directory.GetCurrentDirectory(), "settings", "persistency.json"), Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(publisherNodeConfiguration, Formatting.Indented)), cancellationToken).ConfigureAwait(false) == null)
                {
                    _logger.LogError("Could not store persistency file. Published nodes won't be persisted!");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Update of persistency file failed.");
            }
        }

        public async Task<ReferenceDescriptionCollection> Browse(string endpointUrl, string username, string password, BrowseDescription nodeToBrowse, bool throwOnError)
        {
            // find or create the session
            Session session = await ConnectSessionAsync(
                endpointUrl,
                username,
                password
            ).ConfigureAwait(false);

            if (session == null)
            {
                // couldn't create the session
                throw new Exception($"Could not create session for endpoint {endpointUrl}!");
            }

            return Browse(session, nodeToBrowse, throwOnError);
        }
   

        private static ReferenceDescriptionCollection Browse(Session session, BrowseDescription nodeToBrowse, bool throwOnError)
        {
            try
            { 
                ReferenceDescriptionCollection references = new ReferenceDescriptionCollection();

                // construct browse request.
                BrowseDescriptionCollection nodesToBrowse = new BrowseDescriptionCollection
                {
                    nodeToBrowse
                };

                // start the browse operation.
                session.Browse(
                    null,
                    null,
                    0,
                    nodesToBrowse,
                    out BrowseResultCollection results,
                    out DiagnosticInfoCollection diagnosticInfos);

                ClientBase.ValidateResponse(results, nodesToBrowse);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToBrowse);

                do
                {
                    // check for error.
                    if (StatusCode.IsBad(results[0].StatusCode))
                    {
                        break;
                    }

                    // process results.
                    for (int i = 0; i < results[0].References.Count; i++)
                    {
                        references.Add(results[0].References[i]);
                    }

                    // check if all references have been fetched.
                    if (results[0].References.Count == 0 || results[0].ContinuationPoint == null)
                    {
                        break;
                    }

                    // continue browse operation.
                    ByteStringCollection continuationPoints = new ByteStringCollection
                    {
                        results[0].ContinuationPoint
                    };

                    session.BrowseNext(
                        null,
                        false,
                        continuationPoints,
                        out results,
                        out diagnosticInfos);

                    ClientBase.ValidateResponse(results, continuationPoints);
                    ClientBase.ValidateDiagnosticInfos(diagnosticInfos, continuationPoints);
                }
                while (true);

                // return complete list.
                return references;
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
                    Session session = await ConnectSessionAsync(
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
                            DataValue value = session.ReadValue(ExpandedNodeId.ToNodeId(nodeReference.NodeId, session.NamespaceUris));
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
            ServerPushConfigurationClient serverPushClient = new(_app.UAApplicationInstance.ApplicationConfiguration);

            serverPushClient.AdminCredentials = new UserIdentity(adminUsername, adminPassword);

            await serverPushClient.Connect(endpointURL).ConfigureAwait(false);

            byte[] unusedNonce = new byte[0];
            byte[] certificateRequest = serverPushClient.CreateSigningRequest(
                NodeId.Null,
                serverPushClient.ApplicationCertificateType,
            string.Empty,
            false,
            unusedNonce);

            X509Certificate2 certificate = ProcessSigningRequest(
                serverPushClient.Session.ServerUris.ToArray()[0],
                null,
                certificateRequest);

            byte[][] issuerCertificates = new byte[1][];
            issuerCertificates[0] = _app.IssuerCert.Export(X509ContentType.Cert);

            serverPushClient.UpdateCertificate(
                NodeId.Null,
                serverPushClient.ApplicationCertificateType,
                certificate.Export(X509ContentType.Pfx),
                string.Empty,
                new byte[0],
                issuerCertificates);

            // store in our own trust list
            await _app.UAApplicationInstance.AddOwnCertificateToTrustedStoreAsync(certificate, CancellationToken.None).ConfigureAwait(false);

            // update trust list on server
            TrustListDataType trustList = GetTrustLists();
            serverPushClient.UpdateTrustList(trustList);

            serverPushClient.ApplyChanges();

            serverPushClient.Disconnect();
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

        private TrustListDataType GetTrustLists()
        {
            ByteStringCollection trusted = new ByteStringCollection();
            ByteStringCollection trustedCrls = new ByteStringCollection();
            ByteStringCollection issuers = new ByteStringCollection();
            ByteStringCollection issuersCrls = new ByteStringCollection();

            CertificateTrustList ownTrustList = _app.UAApplicationInstance.ApplicationConfiguration.SecurityConfiguration.TrustedPeerCertificates;
            foreach (X509Certificate2 cert in ownTrustList.GetCertificates().GetAwaiter().GetResult())
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
            Session session = null;
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
                NodeId closeNodeId = new(WoTAssetFileType_CloseAndUpdate, (ushort)session.NamespaceUris.GetIndex("http://opcfoundation.org/UA/WoT-Con/"));
                NodeId parentNodeId = new(WoTAssetConnectionManagement, (ushort)session.NamespaceUris.GetIndex("http://opcfoundation.org/UA/WoT-Con/"));

                Variant assetId = new(string.Empty);

                StatusCode status = new StatusCode(0);
                assetId = ExecuteCommand(session, createNodeId, parentNodeId, assetName, null, out status);
                if (StatusCode.IsNotGood(status))
                {
                    if (status == StatusCodes.BadBrowseNameDuplicated)
                    {
                        // delete existing asset first
                        assetId = ExecuteCommand(session, deleteNodeId, parentNodeId, new NodeId(assetId.Value.ToString()), null, out status);
                        if (StatusCode.IsNotGood(status))
                        {
                            throw new Exception(status.ToString());
                        }

                        // now try again
                        assetId = ExecuteCommand(session, createNodeId, parentNodeId, assetName, null, out status);
                        if (StatusCode.IsNotGood(status))
                        {
                            throw new Exception(status.ToString());
                        }
                    }
                    else
                    {
                        throw new Exception(status.ToString());
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
                fileHandle = ExecuteCommand(session, MethodIds.FileType_Open, fileId, (byte)6, null, out status);
                if (StatusCode.IsNotGood(status))
                {
                    throw new Exception(status.ToString());
                }

                for (int i = 0; i < bytes.Length; i += 3000)
                {
                    byte[] chunk = bytes.AsSpan(i, Math.Min(3000, bytes.Length - i)).ToArray();

                    ExecuteCommand(session, MethodIds.FileType_Write, fileId, fileHandle, chunk, out status);
                    if (StatusCode.IsNotGood(status))
                    {
                        throw new Exception(status.ToString());
                    }
                }

                Variant result = ExecuteCommand(session, closeNodeId, fileId, fileHandle, null, out status);
                if (StatusCode.IsNotGood(status))
                {
                    throw new Exception(status.ToString() + ": " + result.ToString());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);

                if ((session != null) && (fileId != null) && (fileHandle != null))
                {
                    ExecuteCommand(session, MethodIds.FileType_Close, fileId, fileHandle, null, out StatusCode status);
                }

                throw;
            }
            finally
            {
                if (session != null)
                {
                    if (session.Connected)
                    {
                        session.Close();
                    }

                    session.Dispose();
                }
            }
        }

        private Variant ExecuteCommand(Session session, NodeId nodeId, NodeId parentNodeId, object argument1, object argument2, out StatusCode status)
        {
            try
            {
                CallMethodRequestCollection requests = new CallMethodRequestCollection
                {
                    new CallMethodRequest
                    {
                        ObjectId = parentNodeId,
                        MethodId = nodeId,
                        InputArguments = new VariantCollection { new Variant(argument1) }
                    }
                };

                if (argument1 != null)
                {
                    requests[0].InputArguments = new VariantCollection { new Variant(argument1) };
                }

                if ((argument1 != null) && (argument2 != null))
                {
                    requests[0].InputArguments.Add(new Variant(argument2));
                }

                CallMethodResultCollection results;
                DiagnosticInfoCollection diagnosticInfos;

                ResponseHeader responseHeader = session.Call(
                    null,
                    requests,
                    out results,
                    out diagnosticInfos);

                ClientBase.ValidateResponse(results, requests);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, requests);

                status = new StatusCode(0);
                if ((results != null) && (results.Count > 0))
                {
                    status = results[0].StatusCode;

                    if (StatusCode.IsBad(results[0].StatusCode) && (responseHeader.StringTable != null) && (responseHeader.StringTable.Count > 0))
                    {
                        return responseHeader.StringTable[0];
                    }

                    if ((results[0].OutputArguments != null) && (results[0].OutputArguments.Count > 0))
                    {
                        return results[0].OutputArguments[0];
                    }
                }

                return new Variant(string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Executing OPC UA command failed!");
                throw;
            }
        }

        private SimpleAttributeOperandCollection ConstructSelectClauses(Session session)
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
            CollectFields(session, ObjectTypeIds.BaseEventType, selectClauses);

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

        private void CollectFields(Session session, NodeId eventTypeId, SimpleAttributeOperandCollection eventFields)
        {
            // get the supertypes.
            ReferenceDescriptionCollection supertypes = BrowseSuperTypes(session, eventTypeId, false);

            if (supertypes == null)
            {
                return;
            }

            // process the types starting from the top of the tree.
            Dictionary<NodeId, QualifiedNameCollection> foundNodes = new Dictionary<NodeId, QualifiedNameCollection>();
            QualifiedNameCollection parentPath = new QualifiedNameCollection();

            for (int i = supertypes.Count - 1; i >= 0; i--)
            {
                CollectFields(session, (NodeId)supertypes[i].NodeId, parentPath, eventFields, foundNodes);
            }

            // collect the fields for the selected type.
            CollectFields(session, eventTypeId, parentPath, eventFields, foundNodes);
        }

        private void CollectFields(
            Session session,
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

            ReferenceDescriptionCollection children = Browse(session, nodeToBrowse, false);

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
                QualifiedNameCollection browsePath = new QualifiedNameCollection(parentPath);
                browsePath.Add(child.BrowseName);

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
                    CollectFields(session, (NodeId)child.NodeId, browsePath, eventFields, foundNodes);
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

        public static ReferenceDescriptionCollection BrowseSuperTypes(Session session, NodeId typeId, bool throwOnError)
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

                ReferenceDescriptionCollection references = Browse(session, nodeToBrowse, throwOnError);

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
                    references = Browse(session, nodeToBrowse, throwOnError);
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
