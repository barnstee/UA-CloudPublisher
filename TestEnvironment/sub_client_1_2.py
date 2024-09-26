"""
MQTT Subscribers for receiving messages.
"""

import config
import paho.mqtt.client as mqtt


def on_connect(client, userdata, flags, rc):  # pylint: disable=unused-argument
    """Callback function for when the client connects to the broker."""
    print("Connected with result code " + str(rc))
    client.subscribe(topic="data")


def on_message_subscriber1(client, userdata, msg):  # pylint: disable=unused-argument
    """Callback function for when a message is received by subscriber 1."""
    print(f"Subscriber 1 received: {msg.payload.decode()}")


def on_message_subscriber2(client, userdata, msg):  # pylint: disable=unused-argument
    """Callback function for when a message is received by subscriber 2."""
    print(f"Subscriber 2 received: {msg.payload.decode()}")


def setup_subscriber(on_message_callback):
    """Sets up the MQTT client and starts the loop with the given message callback."""
    sub_client = mqtt.Client()
    sub_client.connect(host=config.BROKER, port=config.PORT, keepalive=config.TIMELIVE)
    sub_client.on_connect = on_connect
    sub_client.on_message = on_message_callback
    sub_client.loop_start()
    return sub_client


def main():
    """Main function to set up both subscribers."""
    subscriber1 = setup_subscriber(on_message_subscriber1)
    subscriber2 = setup_subscriber(on_message_subscriber2)

    # Keep the script running
    try:
        while True:
            pass
    except KeyboardInterrupt:
        subscriber1.loop_stop()
        subscriber2.loop_stop()
        print("Stopped...")


if __name__ == "__main__":
    main()
