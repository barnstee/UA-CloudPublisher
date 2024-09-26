"""
MQTT Subscriber 1 for receiving messages.
"""

import config  # Import the configuration settings
import paho.mqtt.client as mqtt


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
    sub_client.connect(host=config.BROKER, port=config.PORT, keepalive=config.TIMELIVE)
    sub_client.on_connect = on_connect
    sub_client.on_message = on_message
    sub_client.loop_forever()


if __name__ == "__main__":
    main()
