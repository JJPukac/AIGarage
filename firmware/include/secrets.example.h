#pragma once

// Copy to secrets.h, which is ignored by Git. Use separate broker credentials
// for ESP32 and mobile, and restrict each account to this device's topics.
#define WIFI_SSID "YOUR_WIFI"
#define WIFI_PASSWORD "YOUR_WIFI_PASSWORD"
#define MQTT_HOST "YOUR_CLUSTER.s1.eu.hivemq.cloud"
#define MQTT_PORT 8883
#define MQTT_USER "garage-device"
#define MQTT_PASSWORD "YOUR_BROKER_PASSWORD"
#define MQTT_ROOT "aigarage/garage1"

// Generate 32 random bytes and provision the same 64-character lowercase
// hexadecimal secret in the mobile app. Do not commit or log the real key.
#define HMAC_KEY_HEX "REPLACE_WITH_64_LOWERCASE_HEX_CHARACTERS"

// Use the CA certificate supplied/verified for your broker's certificate chain.
// Never use setInsecure(). Hostname and validity checks stay enabled.
static const char MQTT_CA[] = R"PEM(
-----BEGIN CERTIFICATE-----
REPLACE_WITH_VERIFIED_BROKER_ROOT_CA_CERTIFICATE
-----END CERTIFICATE-----
)PEM";
