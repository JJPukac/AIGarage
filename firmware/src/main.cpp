#include <Arduino.h>
#include <Adafruit_BME280.h>
#include <ArduinoJson.h>
#include <PubSubClient.h>
#include <WiFi.h>
#include <WiFiClientSecure.h>
#include <Wire.h>
#include <esp_system.h>
#include <mbedtls/md.h>
#include <time.h>
#include "GarageSafety.h"

#if __has_include("secrets.h")
#include "secrets.h"
#else
// Allows a credential-free build. Invalid placeholders fail closed at startup.
#include "secrets.example.h"
#endif

namespace {
constexpr int RelayPin = 26;
constexpr int ReedPin = 27;
constexpr int RelayOff = HIGH;
constexpr int RelayOn = LOW;
constexpr uint32_t TelemetryIntervalMs = 2000;
constexpr uint32_t ReconnectIntervalMs = 5000;
constexpr size_t MaxCommandBytes = 1024;

WiFiClientSecure tls;
PubSubClient mqtt(tls);
Adafruit_BME280 bme;
aigarage::GarageSafety safety;

const String commandTopic = String(MQTT_ROOT) + "/command";
const String telemetryTopic = String(MQTT_ROOT) + "/telemetry";
const String ackTopic = String(MQTT_ROOT) + "/ack";

uint8_t hmacKey[32] = {};
String sessionId;
String clientId;
uint32_t telemetrySequence = 0;
uint32_t lastConnectAt = 0;
uint32_t lastTelemetryAt = 0;
bool configured = false;
bool sensorReady = false;
bool commandPending = false;
int64_t pendingSequence = 0;
int64_t pendingExpiry = 0;
int64_t pulsingSequence = 0;

int LowerHexDigit(char value) {
    if (value >= '0' && value <= '9') return value - '0';
    if (value >= 'a' && value <= 'f') return value - 'a' + 10;
    return -1;
}

bool LoadConfiguration() {
    const char* key = HMAC_KEY_HEX;
    if (strlen(key) != 64 || strstr(MQTT_CA, "REPLACE_") != nullptr ||
        strstr(MQTT_HOST, "YOUR_") != nullptr ||
        strstr(WIFI_SSID, "YOUR_") != nullptr || MQTT_PORT <= 0 || MQTT_PORT > 65535)
        return false;

    for (size_t i = 0; i < sizeof(hmacKey); ++i) {
        const int high = LowerHexDigit(key[i * 2]);
        const int low = LowerHexDigit(key[i * 2 + 1]);
        if (high < 0 || low < 0) return false;
        hmacKey[i] = static_cast<uint8_t>((high << 4) | low);
    }
    // Catch a commonly copied invalid placeholder key.
    uint8_t anyNonzero = 0;
    for (uint8_t value : hmacKey) anyNonzero |= value;
    return anyNonzero != 0;
}

bool ComputeMac(const String& topic, const String& body, uint8_t output[32]) {
    const String input = topic + "\n" + body;
    const mbedtls_md_info_t* info = mbedtls_md_info_from_type(MBEDTLS_MD_SHA256);
    return info != nullptr && mbedtls_md_hmac(
        info, hmacKey, sizeof(hmacKey),
        reinterpret_cast<const uint8_t*>(input.c_str()), input.length(), output) == 0;
}

String MacHex(const String& topic, const String& body) {
    uint8_t digest[32];
    if (!ComputeMac(topic, body, digest)) return "";
    char output[65];
    for (size_t i = 0; i < sizeof(digest); ++i)
        snprintf(output + i * 2, 3, "%02x", digest[i]);
    return String(output);
}

bool VerifyMac(const String& body, const String& signature) {
    if (signature.length() != 64) return false;
    uint8_t expected[32];
    if (!ComputeMac(commandTopic, body, expected)) return false;
    // Do not return at the first differing byte of a well-formed signature.
    uint8_t difference = 0;
    for (size_t i = 0; i < sizeof(expected); ++i) {
        const int high = LowerHexDigit(signature[i * 2]);
        const int low = LowerHexDigit(signature[i * 2 + 1]);
        if (high < 0 || low < 0) return false;
        difference |= expected[i] ^ static_cast<uint8_t>((high << 4) | low);
    }
    return difference == 0;
}

void PublishSigned(const String& topic, const String& body) {
    if (!mqtt.connected()) return;
    const String mac = MacHex(topic, body);
    if (mac.isEmpty()) return;
    StaticJsonDocument<1024> envelope;
    envelope["body"] = body;
    envelope["mac"] = mac;
    if (envelope.overflowed()) return;
    String wire;
    serializeJson(envelope, wire);
    // No retained actuator commands, telemetry or acknowledgements.
    mqtt.publish(topic.c_str(), wire.c_str(), false);
}

void PublishTelemetry() {
    StaticJsonDocument<512> payload;
    payload["boot"] = sessionId;
    payload["seq"] = ++telemetrySequence;
    payload["ts"] = static_cast<int64_t>(time(nullptr));
    payload["door"] = safety.DoorState(millis());

    if (sensorReady) {
        const float temperature = bme.readTemperature();
        const float humidity = bme.readHumidity();
        const float pressure = bme.readPressure() / 100.0F;
        if (isfinite(temperature) && isfinite(humidity) && isfinite(pressure)) {
            payload["temperatureC"] = temperature;
            payload["humidityPct"] = humidity;
            payload["pressureHpa"] = pressure;
        }
    }

    String body;
    serializeJson(payload, body);
    PublishSigned(telemetryTopic, body);
    lastTelemetryAt = millis();
}

const char* ResultName(aigarage::OpenResult result) {
    switch (result) {
    case aigarage::OpenResult::Pulsed: return "pulsed";
    case aigarage::OpenResult::RejectedReplay: return "rejected_replay";
    case aigarage::OpenResult::RejectedExpired: return "rejected_expired";
    default: return "rejected_state";
    }
}

void PublishAck(int64_t sequence, aigarage::OpenResult result) {
    StaticJsonDocument<256> payload;
    payload["boot"] = sessionId;
    payload["seq"] = sequence;
    payload["ts"] = static_cast<int64_t>(time(nullptr));
    payload["result"] = ResultName(result);
    String body;
    serializeJson(payload, body);
    PublishSigned(ackTopic, body);
}

void OnCommand(char* topic, byte* bytes, unsigned int length) {
    if (String(topic) != commandTopic || length == 0 ||
        length > MaxCommandBytes || commandPending)
        return;

    StaticJsonDocument<1536> envelope;
    if (deserializeJson(envelope, bytes, length,
                        DeserializationOption::NestingLimit(3)) ||
        !envelope.is<JsonObject>() || envelope.size() != 2 ||
        !envelope["body"].is<const char*>() ||
        !envelope["mac"].is<const char*>())
        return;

    const String body = envelope["body"].as<String>();
    const String mac = envelope["mac"].as<String>();
    if (body.length() == 0 || body.length() > 384 ||
        !VerifyMac(body, mac))
        return;

    StaticJsonDocument<512> command;
    if (deserializeJson(command, body, DeserializationOption::NestingLimit(2)) ||
        !command.is<JsonObject>() || command.size() != 4 ||
        !command["boot"].is<const char*>() ||
        !command["action"].is<const char*>() ||
        !command["seq"].is<int64_t>() ||
        !command["expires"].is<int64_t>())
        return;

    if (String(command["boot"].as<const char*>()) != sessionId ||
        String(command["action"].as<const char*>()) != "open")
        return;

    pendingSequence = command["seq"].as<int64_t>();
    pendingExpiry = command["expires"].as<int64_t>();
    if (pendingSequence <= 0 || pendingExpiry <= 0) return;
    // The callback never energizes the relay or does a blocking publish.
    commandPending = true;
}

void NewCommandSession() {
    uint8_t nonce[16];
    // Wi-Fi is active here, providing the hardware RNG entropy source.
    esp_fill_random(nonce, sizeof(nonce));
    char hex[33];
    for (size_t i = 0; i < sizeof(nonce); ++i)
        snprintf(hex + i * 2, 3, "%02x", nonce[i]);
    sessionId = hex;
    telemetrySequence = 0;
    commandPending = false;
    safety.NewCommandSession();
}
} // namespace

void setup() {
    // Hardware pull-up/inactive relay bias is still mandatory during reset.
    digitalWrite(RelayPin, RelayOff);
    pinMode(RelayPin, OUTPUT);
    pinMode(ReedPin, INPUT_PULLUP);
    Serial.begin(115200);

    configured = LoadConfiguration();
    if (!configured) {
        Serial.println("Provision firmware/include/secrets.h; relay remains OFF.");
        return;
    }

    Wire.begin(21, 22);
    Wire.setTimeOut(50);
    sensorReady = bme.begin(0x76) || bme.begin(0x77);
    if (!sensorReady) Serial.println("BME280 not found; readings will be absent.");

    tls.setCACert(MQTT_CA);
    tls.setHandshakeTimeout(10);
    mqtt.setServer(MQTT_HOST, MQTT_PORT);
    mqtt.setCallback(OnCommand);
    if (!mqtt.setBufferSize(1536)) {
        configured = false;
        Serial.println("MQTT allocation failed; relay remains OFF.");
        return;
    }
    mqtt.setSocketTimeout(5);
    mqtt.setKeepAlive(30);

    char id[32];
    snprintf(id, sizeof(id), "aigarage-%012llx",
             static_cast<unsigned long long>(ESP.getEfuseMac()));
    clientId = id;
    WiFi.setAutoReconnect(true);
    WiFi.begin(WIFI_SSID, WIFI_PASSWORD);
    // Initial time synchronization is needed for CA validity and expiration.
    configTime(0, 0, "pool.ntp.org", "time.cloudflare.com");
}

void loop() {
    // Absolutely no Wi-Fi/MQTT/BME work while the relay is energized.
    // GPIO is switched off BEFORE publishing an acknowledgement.
    if (safety.IsPulsing()) {
        if (safety.PulseDueToEnd(millis())) {
            digitalWrite(RelayPin, RelayOff);
            safety.CompletePulse();
            PublishAck(pulsingSequence, aigarage::OpenResult::Pulsed);
            PublishTelemetry();
        } else {
            delay(1);
        }
        return;
    }

    if (!configured) {
        delay(100);
        return;
    }

    safety.Observe(digitalRead(ReedPin) == LOW, millis());

    if (WiFi.status() == WL_CONNECTED &&
        time(nullptr) >= aigarage::GarageSafety::MinimumValidEpoch &&
        !mqtt.connected() &&
        static_cast<uint32_t>(millis() - lastConnectAt) >= ReconnectIntervalMs) {
        lastConnectAt = millis();
        if (mqtt.connect(clientId.c_str(), MQTT_USER, MQTT_PASSWORD)) {
            // PubSubClient hides MQTT retain flag. Rotate nonce BEFORE
            // subscribing so commands from any older session fail verification.
            NewCommandSession();
            if (!mqtt.subscribe(commandTopic.c_str(), 1)) {
                mqtt.disconnect();
            } else {
                PublishTelemetry();
            }
        }
    }

    if (mqtt.connected()) {
        mqtt.loop();
        if (commandPending) {
            commandPending = false;
            // Re-read after potentially blocking MQTT operations; never rely on
            // an old debounced closed reading if the input just changed.
            const uint32_t now = millis();
            const bool closed = digitalRead(ReedPin) == LOW;
            safety.Observe(closed, now);
            const auto result = safety.RequestOpen(
                pendingSequence, pendingExpiry,
                static_cast<int64_t>(time(nullptr)), now, closed);

            if (result == aigarage::OpenResult::Pulsed) {
                pulsingSequence = pendingSequence;
                digitalWrite(RelayPin, RelayOn);
                safety.MarkPulseStarted(millis());
                return;
            }
            PublishAck(pendingSequence, result);
        }

        if (static_cast<uint32_t>(millis() - lastTelemetryAt) >= TelemetryIntervalMs)
            PublishTelemetry();
    }
    delay(1);
}
