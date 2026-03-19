#include <BLEDevice.h>
#include <BLEUtils.h>
#include <BLEScan.h>
#include <BLEAdvertisedDevice.h>

/*
 * ForceOSC.ino
 * Lit un capteur de force via HX711.
 * Envoie les données en Serial USB (toujours) et OSC/WiFi (si connecté).
 *
 * Carte : Adafruit Feather ESP32-S3 4MB Flash 2MB PSRAM (5477)
 *
 * Bibliothèques requises :
 *   - HX711 by Bogdan Necula (https://github.com/bogde/HX711)
 *   - OSC by CNMAT (https://github.com/CNMAT/OSC)
 *   - Adafruit NeoPixel
 *
 * Connexions HX711 :
 *   DAT -> pin 12 / 10
 *   CLK -> pin 13 / 11
 *
 * LED APA106 :
 *   DIN -> pin 5 (via 330Ω)
 *   VCC -> 5V
 *   GND -> GND
 *
 * OSC messages envoyés (port 8000) :
 *   /force  float
 *   /stomp  float
 *
 * OSC messages reçus (port 8001) :
 *   /ping
 */

#include <HX711.h>
#include <WiFi.h>
#include <WiFiUdp.h>
#include <OSCMessage.h>
#include <Adafruit_NeoPixel.h>

// ─── LED ─────────────────────────────────────────────────────────────────────
#define LED_PIN   5
#define LED_COUNT 1
Adafruit_NeoPixel led(LED_COUNT, LED_PIN, NEO_RGB + NEO_KHZ800);

const uint32_t COL_OFF    = 0x000000;
const uint32_t COL_WHITE  = 0xFFFFFF;
const uint32_t COL_BLUE   = 0x0000FF;
const uint32_t COL_GREEN  = 0x00FF00;
const uint32_t COL_RED    = 0xFF0000;
const uint32_t COL_YELLOW = 0xFFC800;

bool          flashActive = false;
unsigned long flashEnd    = 0;
uint32_t      flashColor  = COL_OFF;

// Démarre un flash — la couleur de base est toujours OFF (éteinte entre les flashs)
void ledFlash(uint32_t color, unsigned long durationMs) {
  flashActive = true;
  flashEnd    = millis() + durationMs;
  flashColor  = color;
  led.setPixelColor(0, color);
  led.show();
}

// Appeler chaque loop — gère la fin du flash
void ledUpdate() {
  if (flashActive && millis() > flashEnd) {
    flashActive = false;
    led.setPixelColor(0, COL_OFF);
    led.show();
  }
}

// Allume une couleur fixe (interrompt un flash en cours)
void ledSet(uint32_t color) {
  flashActive = false;
  led.setPixelColor(0, color);
  led.show();
}

// ─── Batterie ─────────────────────────────────────────────────────────────────
// Feather ESP32-S3 : VBAT diviseur résistif sur A13 (GPIO2)
#define VBAT_PIN A13
const float VBAT_LOW = 3.5f;  // ~20% LiPo
const float VBAT_MAX = 4.2f;

float readBatteryPct() {
  // Diviseur 1:2 sur le Feather → on multiplie par 2
  float v = analogRead(VBAT_PIN) * 3.3f / 4095.0f * 2.0f;
  return (v - VBAT_LOW) / (VBAT_MAX - VBAT_LOW) * 100.0f;
}

// ─── WiFi ────────────────────────────────────────────────────────────────────
const char* WIFI_SSID = "Le_Moulin_2";
const char* WIFI_PASS = "venasque";

const unsigned int OSC_OUT_PORT = 8000;
const unsigned int OSC_IN_PORT  = 8001;

WiFiUDP udp;
IPAddress broadcastIP(255, 255, 255, 255);
IPAddress unityIP(0, 0, 0, 0);
bool      wifiOK = false;

// ─── HX711 ───────────────────────────────────────────────────────────────────
#define LOADCELL_DAT_PIN  12
#define LOADCELL_CLK_PIN  13
#define LOADCELL2_DAT_PIN 10
#define LOADCELL2_CLK_PIN 11

float CALIBRATION_FACTOR  = 12205.0f;
float CALIBRATION_FACTOR2 = 11035.0f;

const unsigned long SEND_INTERVAL_MS = 12;

#define MED_SIZE 5
float medBuf[MED_SIZE];
int   medIdx = 0;

float getMedian() {
  float tmp[MED_SIZE];
  memcpy(tmp, medBuf, sizeof(tmp));
  for (int i = 0; i < MED_SIZE - 1; i++)
    for (int j = 0; j < MED_SIZE - 1 - i; j++)
      if (tmp[j] > tmp[j+1]) { float t = tmp[j]; tmp[j] = tmp[j+1]; tmp[j+1] = t; }
  return tmp[MED_SIZE / 2];
}

// ─── Détection stomp ─────────────────────────────────────────────────────────
const float JUMP_RATE_THRESHOLD = 1000.0f;
const float JUMP_PEAK_MIN       = 25.0f;
const float PEAK_ABOVE_BASELINE = 12.0f;

const unsigned long STOMP_COOLDOWN_MS = 800;
const unsigned long PEAK_WINDOW_MS    = 600;

unsigned long graceUntil = 0;
const unsigned long GRACE_MS = 2000;

HX711 scale;
HX711 scale2;
bool  readScale1 = true;

unsigned long lastSendTime  = 0;
unsigned long lastStompTime = 0;
unsigned long lastOscForce  = 0;
unsigned long lastHeartbeat = 0;
unsigned long lastBatCheck  = 0;
const unsigned long OSC_FORCE_INTERVAL_MS = 50;
const unsigned long HEARTBEAT_MS          = 10000;
const unsigned long BAT_CHECK_MS          = 30000;

float prevRaw     = 0.0f;
float prevMed     = 0.0f;
float rawBefore   = 0.0f;
float rawBaseline = 0.0f;
float prevOther   = 0.0f;
const float BASELINE_ALPHA = 0.002f;

bool          jumpRising     = false;
float         jumpPeak       = 0.0f;
float         rawAtJumpStart = 0.0f;
unsigned long jumpPeakUntil  = 0;

bool lowBattery = false;

// ─── OSC ─────────────────────────────────────────────────────────────────────
void sendOSC(const char* address, float value) {
  if (!wifiOK) return;
  OSCMessage msg(address);
  msg.add(value);
  IPAddress dest = (unityIP == IPAddress(0,0,0,0)) ? broadcastIP : unityIP;
  udp.beginPacket(dest, OSC_OUT_PORT);
  msg.send(udp);
  udp.endPacket();
}

void checkIncoming() {
  if (!wifiOK) return;
  int size = udp.parsePacket();
  if (size <= 0) return;
  OSCMessage msg;
  while (size--) msg.fill(udp.read());
  if (!msg.hasError() && msg.fullMatch("/ping")) {
    unityIP = udp.remoteIP();
    OSCMessage pong("/pong");
    udp.beginPacket(unityIP, OSC_OUT_PORT);
    pong.send(udp);
    udp.endPacket();
  }
}

// ─── Setup ───────────────────────────────────────────────────────────────────
void setup() {
  Serial.begin(115200);
  while (!Serial && millis() < 3000) {}

  led.begin();
  led.setBrightness(5);
  led.clear();
  led.show(); // éteindre immédiatement pour écraser l'état parasite

  // Flash blanc boot
  led.setPixelColor(0, COL_WHITE);
  led.show();
  Serial.println("LED: flash blanc (boot)");
  delay(300);
  led.setPixelColor(0, COL_OFF);
  led.show();

  // Clignotement bleu pendant recherche WiFi
  WiFi.begin(WIFI_SSID, WIFI_PASS);
  unsigned long wifiStart = millis();
  bool blinkState = false;
  while (WiFi.status() != WL_CONNECTED && millis() - wifiStart < 5000) {
    blinkState = !blinkState;
    led.setPixelColor(0, blinkState ? COL_BLUE : COL_OFF);
    led.show();
    delay(500);
  }
  led.setPixelColor(0, COL_OFF);
  led.show();
  if (WiFi.status() == WL_CONNECTED) {
    wifiOK = true;
    udp.begin(OSC_IN_PORT);
    Serial.print("WiFi OK — IP: ");
    Serial.println(WiFi.localIP());
  } else {
    Serial.println("WiFi KO — serial only");
  }

  scale.begin(LOADCELL_DAT_PIN, LOADCELL_CLK_PIN);
  scale.set_scale(CALIBRATION_FACTOR);
  scale.tare();

  scale2.begin(LOADCELL2_DAT_PIN, LOADCELL2_CLK_PIN);
  scale2.set_scale(CALIBRATION_FACTOR2);
  scale2.tare();

  for (int i = 0; i < MED_SIZE; i++) medBuf[i] = 0.0f;

  // LED éteinte par défaut
  led.setPixelColor(0, COL_OFF);
  led.show();
}

// ─── Loop ────────────────────────────────────────────────────────────────────
void loop() {
  unsigned long now = millis();

  checkIncoming();

  // WiFi reconnect
  if (!wifiOK && WiFi.status() == WL_CONNECTED) {
    wifiOK = true;
    udp.begin(OSC_IN_PORT);
  } else if (wifiOK && WiFi.status() != WL_CONNECTED) {
    wifiOK = false;
  }

  // Batterie toutes les 30s — ignoré si alimenté en USB sans batterie (v > 4.3V)
  if (now - lastBatCheck > BAT_CHECK_MS) {
    lastBatCheck = now;
    float v = analogRead(VBAT_PIN) * 3.3f / 4095.0f * 2.0f;
    if (v > 3.0f && v < 4.3f) { // tension LiPo valide
      lowBattery = (v < VBAT_LOW);
    }
  }

  // Batterie faible → rouge fixe
  if (lowBattery && !flashActive) {
    Serial.println("LED: rouge fixe (batterie < 20%)");
    ledSet(COL_RED);
  }

  // Clignotement bleu à 0.5s tant que pas connecté
  bool connected = wifiOK || Serial;
  static unsigned long lastBlueBlink = 0;
  static bool blueBlinkState = false;
  if (!connected && !lowBattery && now - lastBlueBlink > 500) {
    lastBlueBlink = now;
    blueBlinkState = !blueBlinkState;
    led.setPixelColor(0, blueBlinkState ? COL_BLUE : COL_OFF);
    led.show();
  }

  // Heartbeat vert toutes les 10s si connecté (wifi ou serial)
  if (connected && !lowBattery && now - lastHeartbeat > HEARTBEAT_MS) {
    lastHeartbeat = now;
    Serial.print("LED: flash vert (heartbeat) — wifi:");
    Serial.print(wifiOK ? "OK" : "KO");
    Serial.print(" serial:");
    Serial.println((bool)Serial ? "OK" : "KO");
    ledFlash(COL_GREEN, 200);
  }

  ledUpdate();

  if (now - lastSendTime < SEND_INTERVAL_MS) return;
  float dt = (now - lastSendTime) / 1000.0f;
  lastSendTime = now;

  float rawThis = 0.0f;
  if (readScale1) {
    if (!scale.is_ready()) return;
    rawThis = scale.get_units(1);
  } else {
    if (!scale2.is_ready()) return;
    rawThis = scale2.get_units(1);
  }
  readScale1 = !readScale1;

  float raw = rawThis + prevOther;
  medBuf[medIdx % MED_SIZE] = raw;
  medIdx++;
  float med = getMedian();

  float rawRate = (raw - prevRaw) / dt;
  float medRate = (med - prevMed) / dt;

  if (abs(rawRate) < 20.0f && raw > 2.0f) {
    rawBaseline += BASELINE_ALPHA * (raw - rawBaseline);
  }

  static float prevMedForGrace = 0.0f;
  if (prevMedForGrace < 12.0f && med >= 12.0f && medRate < 300.0f) {
    graceUntil = now + GRACE_MS;
  }
  prevMedForGrace = med;

  bool inGrace = (now < graceUntil);

  bool  stompDetected = false;
  float stompValue    = 0.0f;

  if (!inGrace && !jumpRising && rawRate > JUMP_RATE_THRESHOLD) {
    jumpRising     = true;
    jumpPeak       = raw;
    jumpPeakUntil  = now + PEAK_WINDOW_MS;
    rawAtJumpStart = rawBefore;
  }

  if (jumpRising && raw > jumpPeak) jumpPeak = raw;

  auto evalStomp = [&]() {
    if (jumpPeak > JUMP_PEAK_MIN) {
      bool cas1 = (rawAtJumpStart < 5.0f);
      bool cas2 = (rawAtJumpStart >= 5.0f && jumpPeak > rawAtJumpStart + PEAK_ABOVE_BASELINE);
      if (cas1 || cas2) {
        stompDetected = true;
        stompValue    = jumpPeak;
      }
    }
    jumpRising    = false;
    jumpPeak      = 0.0f;
    jumpPeakUntil = 0;
  };

  if (jumpRising && rawRate < -300.0f) evalStomp();
  if (jumpRising && now > jumpPeakUntil) evalStomp();

  if (med < 2.0f) {
    rawBaseline   = 0.0f;
    jumpRising    = false;
    jumpPeak      = 0.0f;
    jumpPeakUntil = 0;
    graceUntil    = 0;
  }

  if (stompDetected && (now - lastStompTime > STOMP_COOLDOWN_MS)) {
    lastStompTime = now;
    Serial.print("LED: flash jaune (stomp) — ");
    Serial.print(stompValue, 2);
    Serial.println("kg");
    sendOSC("/stomp", stompValue);
    ledFlash(COL_YELLOW, 120);
  }

  if (now - lastOscForce >= OSC_FORCE_INTERVAL_MS) {
    lastOscForce = now;
    sendOSC("/force", med);
  }

  prevOther = rawThis;
  rawBefore = raw;
  prevRaw   = raw;
  prevMed   = med;
}
