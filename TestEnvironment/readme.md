# Description

Docker environment with the following containers:

- UA Cloud Publisher: https://github.com/barnstee/UA-CloudPublisher
- PLC-Simulator: https://github.com/Azure-Samples/iot-edge-opc-plc
- MQTT Broker HiveMQ
- Python mqtt clients to test publishing and subscribing


# UA Cloud Publisher

- Frontend: http://localhost
- Connect to Simulated OPC Server:
    - UA Cloud Publisher as lokal instance (IDE): opc.tcp://localhost:50000/
    - UA Cloud Publisher as container: opc.tcp://plc-sim:50000/
- Configuration (in Frontend):
    - Broker URL: hivemq4
    - Broker Port: 1883
    - Message Topic: data
        - uncheck SAS
        - uncheck TLS

# HiveMQ

- Control center: http://localhost:8888
- Credentials: admin / hivemq

## Testing MQTT Broker with Python Clients

- Test with Python Scripts

    ````
    python.exe .\pub_client_1.py
    python.exe .\sub_client_1.py
    ````

## Further Tips:

- Error " Failed to connect to MQTT broker: Topic should not be empty." &rarr; Connect UA-Cloud-Publisher and OPC-UA Server
- all containers need to be in the same network

    


