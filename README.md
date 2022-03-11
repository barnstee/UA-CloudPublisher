# UA-MQTT-Publisher

A cross-platform OPC UA cloud publisher reference implementation leveraging OPC UA PubSub over MQTT. It runs in a Docker container on standard Docker hosts or on Kubernetes and comes with an easy-to-use web user interface.

## Features

* Cross-plattform - Runs on Windows and Linux
* Runs inside a Docker container
* UI for connecting to, browsing of, reading nodes from and publishing nodes from an OPC UA server
* Uses OPC UA PubSub JSON encoding
* Uses plain MQTT broker as publishing endpoint
* OPC UA Variables publishing
* OPC UA Alarms, Conditions & other events publishing
* OPC UA Event filtering
* OPC UA Complex Types publishing
* OPC UA metadata publishing
* UI for displaying the list of publishes nodes
* UI for displaying diagnostic information
* UI for configuration
* Publishing from the cloud via a connected MQTT broker
* Publishing on data changes or on regular intervals
* Supports publishednodes.json imput file format
* Support for storing configuration files locally or in the cloud
* Support for Store & Forward during Internet connection outages

## Usage

Docker containers are automatically built and published. Simply run the UA-MQTT-Publisher on a Docker-enabled computer via:

`docker run -p 80:80 ghcr.io/barnstee/ua-mqtt-publisher:main`

And then point your browser to <http://yourIPAddress>.

### Persisting Settings

UA-MQTT-Publisher settings and published nodes configuration can be persisted in the Cloud across Docker container restarts by running:

`docker run -e STORAGE_TYPE="Azure" -e STORAGE_CONNECTION_STRING="yourAzureBlobStorageConnectionString" -p 80:80 ghcr.io/barnstee/ua-mqtt-publisher:main`

And then point your browser again to <http://yourIPAddress>.

## Optional Environment Variables

* LOG_FILE_PATH - path to the log file to use. Default is /app/Logs/UA-MQTT-Publisher.log (in the Docker container).
* STORAGE_TYPE - type of storage to use for settings and configuration files. Current options are "Azure". Default is local file storage (under /app/settings/ in the Docker container).
* STORAGE_CONNECTION_STRING - when using STORAGE_TYPE, specifies the connection string to the cloud storage.

## PublishedNodes.json File Format

(All intervals must be specified in milliseconds)

```json
[
 {
  "EndpointUrl": "string", // e.g. "opc.tcp://opcua.example.com/"
  "OpcNodes": [
   {
    "Id": "string", // Expanded Node Id
    "OpcSamplingInterval": 1000,
    "OpcPublishingInterval": 1000,
    "HeartbeatInterval": 0,  // e.g. set if you want to republish quasi static values
    "SkipFirst": false
   }
  ],
  "OpcEvents": [
   {
    "ExpandedNodeId": "string", // e.g. "nsu=http://example.com/Instance/;i=56643"
    "Filter": [
     {
      "OfType": "string" // Expanded node ID of event type to filter by e.g. "nsu=http://opcfoundation.org/UA/MachineTool/;i=39"
     }
    ]
   }
  ],
  "OpcAuthenticationMode": "Anonymous", // or "UsernamePassword"
  "UserName": "string",
  "Password": "string"
 }
]
```

## MQTT Sub-topics for Configuration from the Cloud

### PublishNodes

Payload:
(All intervals must be specified in milliseconds)

```json
{
 "EndpointUrl": "string",
 "OpcNodes": [
  {
   "Id": "string", // Expanded Node Id
   "OpcSamplingInterval": 1000,
   "OpcPublishingInterval": 1000,
   "HeartbeatInterval": 0,
   "SkipFirst": false
  }
 ],
 "OpcEvents": [
  {
   "ExpandedNodeId": "string",
   "Filter": [
    {
     "OfType": "string" // Expanded node ID of event type to filter by
    }
   ]
  }
 ],
 "OpcAuthenticationMode": "Anonymous", // or "UsernamePassword"
 "UserName": "string",
 "Password": "string"
}
```

Response:

```json
{
 [
  "string"
 ]
}
```

### UnpublishNodes

Payload:

```json
{
 "EndpointUrl": "string",
 "OpcNodes": [
  {
   "Id": "string" // Expanded Node Id
  }
 ],
 "OpcEvents": [
  {
   "ExpandedNodeId": "string"
  }
 ]
}
```

Response:

```json
{
 [
  "string"
 ]
}
```

### UnpublishAllNodes

Payload: None

Response:

```json
{
 [
  "string"
 ]
}
```

### GetPublishedNodes

Payload: None

Response:
(All intervals are in milliseconds)

```json
[
 {
  "EndpointUrl": "string",
  "OpcNodes": [
   {
    "Id": "string", // Expanded Node Id
    "OpcSamplingInterval": 1000,
    "OpcPublishingInterval": 1000,
    "HeartbeatInterval": 0,
    "SkipFirst": false
   }
  ],
  "OpcEvents": [
   {
    "ExpandedNodeId": "string",
    "Filter": [
     {
      "OfType": "string"
     }
    ]
   }
  ],
  "OpcAuthenticationMode": "string"
 }
]
```

### GetInfo

Payload: None

Response:

```json
{
 "DiagnosticInfos": [
  {
   "PublisherStartTime": "2022-02-22T22:22:22.222Z",
   "NumberOfOpcSessionsConnected": 0,
   "NumberOfOpcSubscriptionsConnected": 0,
   "NumberOfOpcMonitoredItemsMonitored": 0,
   "MonitoredItemsQueueCount": 0,
   "EnqueueCount": 0,
   "EnqueueFailureCount": 0,
   "NumberOfEvents": 0,
   "MissedSendIntervalCount": 0,
   "TooLargeCount": 0,
   "SentBytes": 0,
   "SentMessages": 0,
   "SentLastTime": "2022-02-22T22:22:22.222Z",
   "FailedMessages": 0,
   "AverageMessageLatency": 0,
   "AverageNotificationsInBrokerMessage": 0
  }
 ]
}
```

## Build Status

[![Docker](https://github.com/barnstee/UA-MQTT-Publisher/actions/workflows/docker-publish.yml/badge.svg)](https://github.com/barnstee/UA-MQTT-Publisher/actions/workflows/docker-publish.yml)
