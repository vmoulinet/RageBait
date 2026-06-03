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
 * Connexions HX711 (4 amplis sommés) :
 *   AMP1 DAT/CLK -> GPIO 5  / 9
 *   AMP2 DAT/CLK -> GPIO 10 / 11
 *   AMP3 DAT/CLK -> A0 / A1
 *   AMP4 DAT/CLK -> A2 / A3
 *
 * LED APA106 :
 *   DIN -> pin 13 (via 330Ω)
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
#define LED_PIN           13   // LED externe APA106
#define LED_ONBOARD_PIN   PIN_NEOPIXEL   // NeoPixel embarquée du Feather (GPIO 33)
#define LED_COUNT         1
Adafruit_NeoPixel led(LED_COUNT, LED_PIN, NEO_RGB + NEO_KHZ800);
Adafruit_NeoPixel ledOnboard(LED_COUNT, LED_ONBOARD_PIN, NEO_GRB + NEO_KHZ800);

// Écrit la même couleur sur les deux LEDs
void ledWrite(uint32_t color) {
  led.setPixelColor(0, color);
  led.show();
  ledOnboard.setPixelColor(0, color);
  ledOnboard.show();
}

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
  ledWrite(color);
}

// Appeler chaque loop — gère la fin du flash
void ledUpdate() {
  if (flashActive && millis() > flashEnd) {
    flashActive = false;
    ledWrite(COL_OFF);
  }
}

// Allume une couleur fixe (interrompt un flash en cours)
void ledSet(uint32_t color) {
  flashActive = false;
  ledWrite(color);
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
const char* WIFI_SSID = "Daddy_router";
const char* WIFI_PASS = "STOMPdaddyHARDER";

const unsigned int OSC_OUT_PORT = 8000;
const unsigned int OSC_IN_PORT  = 8001;

WiFiUDP udp;
IPAddress broadcastIP(255, 255, 255, 255);
IPAddress unityIP(0, 0, 0, 0);
bool      wifiOK = false;

// ─── HX711 ───────────────────────────────────────────────────────────────────
#define LOADCELL1_DAT_PIN 5
#define LOADCELL1_CLK_PIN 9
#define LOADCELL2_DAT_PIN 10
#define LOADCELL2_CLK_PIN 11
#define LOADCELL3_DAT_PIN A0
#define LOADCELL3_CLK_PIN A1
#define LOADCELL4_DAT_PIN A2
#define LOADCELL4_CLK_PIN A3

#define NUM_SCALES 4

float CALIBRATION_FACTOR[NUM_SCALES] = {
  12205.0f,  // AMP1
  11035.0f,  // AMP2
  12000.0f,  // AMP3 — à calibrer
  12000.0f,  // AMP4 — à calibrer
};

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

HX711 scales[NUM_SCALES];
float lastScaleValue[NUM_SCALES] = {0.0f, 0.0f, 0.0f, 0.0f};
int   scaleIdx = 0;

unsigned long lastSendTime  = 0;
unsigned long lastStompTime = 0;
unsigned long lastHeartbeat = 0;
unsigned long lastBatCheck  = 0;
const unsigned long HEARTBEAT_MS = 5000;
const unsigned long BAT_CHECK_MS          = 30000;

float prevRaw     = 0.0f;
float prevMed     = 0.0f;
float rawBefore   = 0.0f;
float rawBaseline = 0.0f;
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
    pong.add((float)WiFi.RSSI());
    udp.beginPacket(unityIP, OSC_OUT_PORT);
    pong.send(udp);
    udp.endPacket();
  }
}

// ─── Setup ───────────────────────────────────────────────────────────────────
void setup() {
  Serial.begin(115200);
  while (!Serial && millis() < 3000) {}

  // Alim NeoPixel embarquée du Feather ESP32-S3
#ifdef NEOPIXEL_POWER
  pinMode(NEOPIXEL_POWER, OUTPUT);
  digitalWrite(NEOPIXEL_POWER, HIGH);
#endif

  led.begin();
  led.setBrightness(5);
  ledOnboard.begin();
  ledOnboard.setBrightness(5);
  ledWrite(COL_OFF); // éteindre immédiatement pour écraser l'état parasite

  // Flash blanc boot
  ledWrite(COL_WHITE);
  Serial.println("LED: flash blanc (boot)");
  delay(300);
  ledWrite(COL_OFF);

  // Clignotement bleu pendant recherche WiFi
  WiFi.begin(WIFI_SSID, WIFI_PASS);
  unsigned long wifiStart = millis();
  bool blinkState = false;
  while (WiFi.status() != WL_CONNECTED && millis() - wifiStart < 5000) {
    blinkState = !blinkState;
    ledWrite(blinkState ? COL_BLUE : COL_OFF);
    delay(500);
  }
  ledWrite(COL_OFF);
  if (WiFi.status() == WL_CONNECTED) {
    wifiOK = true;
    udp.begin(OSC_IN_PORT);
    Serial.print("WiFi OK — IP: ");
    Serial.println(WiFi.localIP());
  } else {
    Serial.println("WiFi KO — serial only");
  }

  const uint8_t datPins[NUM_SCALES] = {LOADCELL1_DAT_PIN, LOADCELL2_DAT_PIN, LOADCELL3_DAT_PIN, LOADCELL4_DAT_PIN};
  const uint8_t clkPins[NUM_SCALES] = {LOADCELL1_CLK_PIN, LOADCELL2_CLK_PIN, LOADCELL3_CLK_PIN, LOADCELL4_CLK_PIN};
  for (int i = 0; i < NUM_SCALES; i++) {
    scales[i].begin(datPins[i], clkPins[i]);
    scales[i].set_scale(CALIBRATION_FACTOR[i]);
    scales[i].tare();
  }

  for (int i = 0; i < MED_SIZE; i++) medBuf[i] = 0.0f;

  // LED éteinte par défaut
  ledWrite(COL_OFF);
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
    ledWrite(blueBlinkState ? COL_BLUE : COL_OFF);
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

  if (!scales[scaleIdx].is_ready()) return;
  lastScaleValue[scaleIdx] = scales[scaleIdx].get_units(1);
  scaleIdx = (scaleIdx + 1) % NUM_SCALES;

  float raw = 0.0f;
  for (int i = 0; i < NUM_SCALES; i++) raw += lastScaleValue[i];
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

  sendOSC("/force", raw);

  rawBefore = raw;
  prevRaw   = raw;
  prevMed   = med;
}
