# UA-MQTT-Publisher
A cross-platform OPC UA cloud publisher reference impelementation leveraging OPC UA PubSub over MQTT, running in a Docker container or on Kubernetes and comes with an easy-to-use web user interface.

## Features
* Cross-plattform - Runs on Windows and Linux
* Runs inside a Docker container
* UI for connecting to, browsing of, reading nodes from and publishing nodes from an OPC UA server
* Uses OPC UA PubSub JSON encoding
* Uses plain MQTT broker as publishing endpoint
* OPC UA Variables publishing
* OPC UA Alarms & Events publishing
* UI for displaying the list of publishes nodes
* UI for diaplaying diagnostic infomration
* UI for configuration
* Publishing from the cloud via a connected MQTT broker
* Publishing on data changes or on regular intervals
* Supports Microsoft OPC Publisher publishesnodes.json imput file format
* Support for storing configuration files locally or in the cloud

## Optional Environment Variables
* LOG_FILE_PATH - path to the log file to use. Default is ./Logs/UA-MQTT-Publisher.log.
* STORAGE_TYPE - type of storage to use for settings and configuration files. Current options are "Azure". Default is local file storage (within the container).
* STORAGE_CONNECTION_STRING - when using STORAGE_TYPE, specifies the connection string to the cloud storage.

## MQTT Sub-topics for Configuration from the Cloud

### PublishNodes
Payload:
```json
{
	"EndpointUrl": "string",
	"OpcNodes": [
		"Id": "string",
		"ExpandedNodeId": "string",
		"OpcSamplingInterval": int,
		"OpcPublishingInterval": int,
		"DisplayName": "string",
		"HeartbeatInterval": int,
		"SkipFirst": bool
	],
	"OpcSessionUserAuthenticationMode": "Anonymous"|"UsernamePassword",
	"UseSecurity": bool,
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
		"Id": "string",
		"ExpandedNodeId": "string",
		"OpcSamplingInterval": int,
		"OpcPublishingInterval": int,
		"DisplayName": "string",
		"HeartbeatInterval": int,
		"SkipFirst": bool
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

### UnPublishAllNodes
Payload:
```json
{
	"EndpointUrl": "string"
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

### GetDiagnosticInfo
Payload: none
Response:
```json
{
	"DiagnosticInfos": [
		"PublisherStartTime": DateTime,
		"NumberOfOpcSessionsConnected": int,
		"NumberOfOpcSubscriptionsConnected": int,
		"NumberOfOpcMonitoredItemsMonitored": int,
		"MonitoredItemsQueueCount": long,
		"EnqueueCount": long,
		"EnqueueFailureCount": long,
		"NumberOfEvents": long,
		"MissedSendIntervalCount": long,
		"TooLargeCount": long,
		"SentBytes": long,
		"SentMessages": long,
		"SentLastTime": DateTime,
		"FailedMessages": long,
		"AverageMessageLatency": long,
		"AverageNotificationsInBrokerMessage": long
	]
}
```

## Build Status

[![Docker](https://github.com/barnstee/UA-MQTT-Publisher/actions/workflows/docker-publish.yml/badge.svg)](https://github.com/barnstee/UA-MQTT-Publisher/actions/workflows/docker-publish.yml)
