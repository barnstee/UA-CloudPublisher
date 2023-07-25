import paho.mqtt.client as mqtt

#broker settings
broker="localhost" # mqtt broker url + port exposed to local
port=1883

#time for Subscriber to live
timelive=60

def on_connect(client, userdata, flags, rc):
  print("Connected with result code "+str(rc))
  client.subscribe(topic="data")

def on_message(client, userdata, msg):
    print(msg.payload.decode())
    
client = mqtt.Client()
client.connect(host=broker,port=port,keepalive=timelive)
client.on_connect = on_connect
client.on_message = on_message
client.loop_forever()