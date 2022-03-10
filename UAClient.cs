
namespace UA.MQTT.Publisher
{
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Opc.Ua;
    using Opc.Ua.Client;
    using Opc.Ua.Client.ComplexTypes;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using UA.MQTT.Publisher.Interfaces;
    using UA.MQTT.Publisher.Models;

    public class UAClient : IUAClient
    {
        private readonly IUAApplication _app;
        private readonly ILogger _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IFileStorage _storage;

        private IMessageSource _trigger;
 
        private List<Session> _sessions = new List<Session>();
        private List<SessionReconnectHandler> _reconnectHandlers = new List<SessionReconnectHandler>();
        private List<PeriodicPublishing> _periodicPublishingList = new List<PeriodicPublishing>();
        private Dictionary<string, uint> _missedKeepAlives = new Dictionary<string, uint>();
        private Dictionary<string, EndpointDescription> _endpointDescriptionCache = new Dictionary<string, EndpointDescription>();
        private readonly Dictionary<Session, ComplexTypeSystem> _complexTypeList = new Dictionary<Session, ComplexTypeSystem>();

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
                UnpublishAllNodes();
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
                lock (_endpointDescriptionCache)
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

            // check there is already a session for the requested endpoint
            lock (_sessions)
            {
                ConfiguredEndpoint configuredEndpoint = new ConfiguredEndpoint(
                    null,
                    selectedEndpoint,
                    EndpointConfiguration.Create()
                );

                foreach (Session session in _sessions)
                {
                    if (session.ConfiguredEndpoint.EndpointUrl == configuredEndpoint.EndpointUrl)
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

            EndpointDescription selectedEndpoint = CoreClientUtils.SelectEndpoint(endpointUrl, true);
            ConfiguredEndpoint configuredEndpoint = new ConfiguredEndpoint(null, selectedEndpoint, EndpointConfiguration.Create());
            _logger.LogInformation("Connecting session on endpoint {endpointUrl}.", configuredEndpoint.EndpointUrl);

            uint timeout = (uint)_app.GetAppConfig().ClientConfiguration.DefaultSessionTimeout;

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
                    _app.GetAppConfig(),
                    configuredEndpoint,
                    true,
                    false,
                    _app.GetAppConfig().ApplicationName,
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

            // register keep alive callback
            newSession.KeepAlive += KeepAliveHandler;

            // add the session to our list
            lock (_sessions)
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

        public void UnpublishAllNodes()
        {
            // loop through all sessions
            lock (_sessions)
            {
                foreach (PeriodicPublishing heartbeat in _periodicPublishingList)
                {
                    heartbeat.Stop();
                    heartbeat.Dispose();
                }
                _periodicPublishingList.Clear();

                while (_sessions.Count > 0)
                {
                    Session session = _sessions[0];
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
            PersistPublishedNodesAsync().GetAwaiter().GetResult();
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

        private void KeepAliveHandler(Session session, KeepAliveEventArgs eventArgs)
        {
            if (eventArgs != null && session != null && session.ConfiguredEndpoint != null)
            {
                try
                {
                    string endpoint = session.ConfiguredEndpoint.EndpointUrl.AbsoluteUri;

                    lock (_missedKeepAlives)
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
                                lock (_reconnectHandlers)
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
                                    lock (_sessions)
                                    {
                                        _sessions.Remove(session);
                                    }

                                    Diagnostics.Singleton.Info.NumberOfOpcSessionsConnected--;
                                    _logger.LogInformation($"RECONNECTING session {session.SessionId}...");
                                    SessionReconnectHandler reconnectHandler = new SessionReconnectHandler();
                                    lock (_reconnectHandlers)
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
            lock (_reconnectHandlers)
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
            if (reconnectHandler == null)
            {
                return;
            }

            // update the session
            Session session = reconnectHandler.Session;
            lock (_sessions)
            {
                _sessions.Add(session);
            }

            Diagnostics.Singleton.Info.NumberOfOpcSessionsConnected++;
            lock (_reconnectHandlers)
            {
                _reconnectHandlers.Remove(reconnectHandler);
            }
            reconnectHandler.Dispose();

            _logger.LogInformation($"RECONNECTED session {session.SessionId}!");
        }

        public async Task PublishNodeAsync(NodePublishingModel nodeToPublish, CancellationToken cancellationToken = default)
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
                int opcPublishingIntervalForNode = (nodeToPublish.OpcPublishingInterval == 0) ? (int)Settings.Singleton.DefaultOpcPublishingInterval : nodeToPublish.OpcPublishingInterval;
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

                    eventFilter.SelectClauses = FilterUtils.ConstructSelectClauses(session);
                    eventFilter.WhereClause = FilterUtils.ConstructWhereClause(ofTypes, EventSeverity.Min);
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

                int opcSamplingIntervalForNode = (nodeToPublish.OpcSamplingInterval == 0) ? (int)Settings.Singleton.DefaultOpcSamplingInterval : nodeToPublish.OpcSamplingInterval;
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

                opcSubscription.AddItem(newMonitoredItem);
                opcSubscription.ApplyChanges();

                // create a heartbeat timer, if required
                if (nodeToPublish.HeartbeatInterval > 0)
                {
                    PeriodicPublishing heartbeat = new PeriodicPublishing(
                        (uint)nodeToPublish.HeartbeatInterval,
                        session,
                        resolvedNodeId,
                        _loggerFactory);

                    lock (_periodicPublishingList)
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

                        _logger.LogError("OPC UA ServiceResultException is {result}. Please check your UA-MQTT-Publisher configuration for this node.", sre.Result);
                        break;

                    default:
                        _logger.LogError("Unhandled OPC UA ServiceResultException {result} when monitoring node {expandedNodeId} on endpoint {endpointUrl}. Continue.",
                            sre.Result,
                            nodeToPublish.ExpandedNodeId,
                            session.ConfiguredEndpoint.EndpointUrl);
                        break;
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "PublishNode: Exception while trying to add node {expandedNodeId} for monitoring.", nodeToPublish.ExpandedNodeId);
            }
        }

        public void UnpublishNode(NodePublishingModel nodeToUnpublish)
        {
            // find the required session
            Session session = FindSession(nodeToUnpublish.EndpointUrl);
            if (session == null)
            {
                throw new ArgumentException("Session for endpoint {endpointUrl} no longer exists!", nodeToUnpublish.EndpointUrl);
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
                lock (_sessions)
                {
                    foreach (Session session in _sessions)
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

                                    lock (_periodicPublishingList)
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

                        publisherConfigurationFileEntries.Add(publisherConfigurationFileEntry);
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
    }
}
