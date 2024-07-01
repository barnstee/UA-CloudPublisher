"""
MQTT Subscriber 2 for receiving messages.
"""

import paho.mqtt.client as mqtt

# Broker settings
BROKER = "localhost"  # MQTT broker URL
PORT = 1883

# Time for Subscriber to live
TIMELIVE = 60


def on_connect(client, userdata, flags, rc):  # pylint: disable=unused-argument
    """Callback function for when the client connects to the broker."""
    print("Connected with result code " + str(rc))
    client.subscribe(topic="data")


def on_message(client, userdata, msg):  # pylint: disable=unused-argument
    """Callback function for when a message is received."""
    print(msg.payload.decode())


def main():
    """Main function to set up the MQTT client and start the loop."""
    sub_client = mqtt.Client()
    sub_client.connect(host=BROKER, port=PORT, keepalive=TIMELIVE)
    sub_client.on_connect = on_connect
    sub_client.on_message = on_message
    sub_client.loop_forever()


if __name__ == "__main__":
    main()
