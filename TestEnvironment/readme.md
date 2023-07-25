# Description

Docker environment with the following containers:

- UA Cloud Publisher: https://github.com/barnstee/UA-CloudPublisher
- PLC-Simulator: https://github.com/Azure-Samples/iot-edge-opc-plc
- MQTT Broker HiveMQ

All containers need to be on the same network

Plus local Python MQTT clients to test publishing and subscribing.

# UA Cloud Publisher

- Frontend: http://localhost
- Connect to Simulated OPC Server: opc.tcp://plc-sim:50000/
- Configuration (in UI):
    - Broker URL: hivemq4
    - Broker Port: 1883
    - Message Topic: data
    - uncheck SAS
    - uncheck TLS

# HiveMQ

- Control center: http://localhost:8888
- Credentials: admin / hivemq

## Testing MQTT Broker with Python Clients

    ````
    python.exe .\pub_client_1.py
    python.exe .\sub_client_1.py
    ````

## Tips:

- Error "Failed to connect to MQTT broker: Topic should not be empty." &rarr; Please Connect UA-Cloud-Publisher and OPC-UA Server.
