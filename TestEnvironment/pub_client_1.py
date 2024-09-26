"""
Simulator device 1 for MQTT message publishing.
"""

import random
import time

import config  # Import the configuration settings
import paho.mqtt.client as paho


def on_publish(client, userdata, result):  # pylint: disable=unused-argument
    """Callback function for when a message is published."""
    print("Device 1: Data published.")


def main():
    """Main function to publish MQTT messages."""
    client = paho.Client(client_id="admin")
    client.on_publish = on_publish
    client.connect(host=config.BROKER, port=config.PORT)

    for i in range(20):
        delay = random.randint(1, 5)
        message = f"Device 1: Data {i}"
        time.sleep(delay)
        client.publish(topic="data", payload=message)

    print("Stopped...")


if __name__ == "__main__":
    main()
