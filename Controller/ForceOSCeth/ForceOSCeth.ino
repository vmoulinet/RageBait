/*
 * ForceOSCeth.ino
 * Version Ethernet + WiFi fallback du ForceOSC.
 *
 * Carte : Adafruit Feather ESP32-S3 4MB Flash 2MB PSRAM (5477)
 *
 * Cascade de connectivite au boot :
 *   1. Ethernet (W5500) : attente link 2s
 *      - Link OK -> DHCP + OSC. Fin.
 *      - Pas de link -> PHY power-down (~3-5mA) et passage au WiFi.
 *   2. WiFi via WiFiManager : portail captif "Daddy_controllerSETUP" si pas de
 *      credentials ou echec de connexion (timeout 60s).
 *   3. Serial only si les deux echouent.
 *
 * Triple power-cycle : couper/remettre l'alim 3x dans une fenetre de 10s
 * efface les credentials WiFi et force le portail captif a reapparaitre.
 *
 * Pins HX711 (4 amplis sommes) :
 *   AMP1 DAT/CLK -> 6 / 9
 *   AMP2 DAT/CLK -> 10 / 11
 *   AMP3 DAT/CLK -> A0 / A1
 *   AMP4 DAT/CLK -> A2 / A3
 *
 * LED APA106 externe : DIN -> 13 (via 330Ω), VCC -> 5V, GND -> GND
 *
 * W5500 (SPI2 par defaut du Feather ESP32-S3) :
 *   MOSI -> GPIO35 (MO sur silkscreen)
 *   MISO -> GPIO37 (MI sur silkscreen)
 *   SCK  -> GPIO36 (SCK sur silkscreen)
 *   CS   -> GPIO14 (A4 sur silkscreen)
 *   RST  -> 3.3V (pullup externe obligatoire sur modules clones)
 *   INT  -> non connecte
 *   VCC  -> 3.3V, GND -> GND
 *
 * OSC envoyes (port 8000) : /force float, /stomp float
 * OSC recus (port 8001) : /ping
 *
 * Bibliotheques :
 *   - HX711 by Bogdan Necula
 *   - Ethernet (Arduino, supporte W5500)
 *   - WiFiManager by tzapu
 *   - OSC by CNMAT / Adrian Freed
 *   - Adafruit NeoPixel
 */

#include <SPI.h>
#include <Ethernet.h>
#include <EthernetUdp.h>
#include <WiFi.h>
#include <WiFiUdp.h>
#include <WiFiManager.h>
#include <OSCMessage.h>
#include <HX711.h>
#include <Adafruit_NeoPixel.h>
#include <Preferences.h>

// ─── LED ─────────────────────────────────────────────────────────────────────
#define LED_PIN           13
#define LED_ONBOARD_PIN   PIN_NEOPIXEL
#define LED_COUNT         1
Adafruit_NeoPixel led(LED_COUNT, LED_PIN, NEO_RGB + NEO_KHZ800);
Adafruit_NeoPixel ledOnboard(LED_COUNT, LED_ONBOARD_PIN, NEO_GRB + NEO_KHZ800);

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
const uint32_t COL_CYAN   = 0x00FFFF;

bool          flashActive = false;
unsigned long flashEnd    = 0;
uint32_t      flashColor  = COL_OFF;

void ledFlash(uint32_t color, unsigned long durationMs) {
  flashActive = true;
  flashEnd    = millis() + durationMs;
  flashColor  = color;
  ledWrite(color);
}

void ledUpdate() {
  if (flashActive && millis() > flashEnd) {
    flashActive = false;
    ledWrite(COL_OFF);
  }
}

void ledSet(uint32_t color) {
  flashActive = false;
  ledWrite(color);
}

// ─── Batterie ─────────────────────────────────────────────────────────────────
#define VBAT_PIN A13
const float VBAT_LOW = 3.5f;
const float VBAT_MAX = 4.2f;

// ─── Reseau : pins + OSC ─────────────────────────────────────────────────────
#define ETH_CS_PIN 14   // GPIO14 = A4 sur Feather S3

const unsigned int OSC_OUT_PORT = 8000;
const unsigned int OSC_IN_PORT  = 8001;

byte mac[] = { 0xDE, 0xAD, 0xBE, 0xEF, 0xFE, 0x02 };

EthernetUDP ethUdp;
WiFiUDP     wifiUdp;
IPAddress   broadcastIP(255, 255, 255, 255);
IPAddress   unityIP(0, 0, 0, 0);

enum NetMode { NET_NONE, NET_ETH, NET_WIFI };
NetMode netMode = NET_NONE;

const unsigned long LINK_UP_WAIT_MS     = 2000;
const unsigned long WIFI_PORTAL_TIMEOUT = 60;

// ─── Triple power-cycle (reset WiFi creds) ───────────────────────────────────
const unsigned long BOOT_GRACE_MS        = 10000;
const int           RESET_BOOT_THRESHOLD = 3;
Preferences bootPrefs;
bool forceResetPortal   = false;
bool bootCounterCleared = false;

// ─── HX711 ───────────────────────────────────────────────────────────────────
#define LOADCELL1_DAT_PIN 6
#define LOADCELL1_CLK_PIN 9
#define LOADCELL2_DAT_PIN 10
#define LOADCELL2_CLK_PIN 11
#define LOADCELL3_DAT_PIN A0
#define LOADCELL3_CLK_PIN A1
#define LOADCELL4_DAT_PIN A2
#define LOADCELL4_CLK_PIN A3

#define NUM_SCALES 4

float CALIBRATION_FACTOR[NUM_SCALES] = {
  12205.0f,
  11035.0f,
  12000.0f,
  12000.0f,
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

// ─── Detection stomp ─────────────────────────────────────────────────────────
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
const unsigned long BAT_CHECK_MS = 30000;

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

// ─── OSC helpers ─────────────────────────────────────────────────────────────
void sendOSC(const char* address, float value) {
  // Toujours emettre en Serial au format parse par monitor.py :
  //   /force -> "FORCE/<val>"   |   /stomp -> "STOMP/<val>"
  // Permet le mode Serial only (NET_NONE) ET sert de debug en reseau.
  if (strcmp(address, "/force") == 0) {
    Serial.print("FORCE/");
    Serial.println(value, 2);
  } else if (strcmp(address, "/stomp") == 0) {
    Serial.print("STOMP/");
    Serial.println(value, 2);
  }

  if (netMode == NET_NONE) return;
  OSCMessage msg(address);
  msg.add(value);
  IPAddress dest = (unityIP == IPAddress(0,0,0,0)) ? broadcastIP : unityIP;
  if (netMode == NET_ETH) {
    ethUdp.beginPacket(dest, OSC_OUT_PORT);
    msg.send(ethUdp);
    ethUdp.endPacket();
  } else {
    wifiUdp.beginPacket(dest, OSC_OUT_PORT);
    msg.send(wifiUdp);
    wifiUdp.endPacket();
  }
}

void handlePing(IPAddress from) {
  unityIP = from;
  OSCMessage pong("/pong");
  // En Ethernet on envoie juste 1.0. En WiFi on garde la convention ForceOSC
  // (RSSI). Le monitor gere les deux.
  float payload = (netMode == NET_WIFI) ? (float)WiFi.RSSI() : 1.0f;
  pong.add(payload);
  if (netMode == NET_ETH) {
    ethUdp.beginPacket(unityIP, OSC_OUT_PORT);
    pong.send(ethUdp);
    ethUdp.endPacket();
  } else {
    wifiUdp.beginPacket(unityIP, OSC_OUT_PORT);
    pong.send(wifiUdp);
    wifiUdp.endPacket();
  }
}

void checkIncoming() {
  if (netMode == NET_ETH) {
    int size = ethUdp.parsePacket();
    if (size <= 0) return;
    IPAddress from = ethUdp.remoteIP();
    OSCMessage msg;
    while (size--) msg.fill(ethUdp.read());
    if (!msg.hasError() && msg.fullMatch("/ping")) handlePing(from);
  } else if (netMode == NET_WIFI) {
    int size = wifiUdp.parsePacket();
    if (size <= 0) return;
    IPAddress from = wifiUdp.remoteIP();
    OSCMessage msg;
    while (size--) msg.fill(wifiUdp.read());
    if (!msg.hasError() && msg.fullMatch("/ping")) handlePing(from);
  }
}

// ─── W5500 PHY power-down ────────────────────────────────────────────────────
void w5500WritePhyCfg(uint8_t value) {
  SPI.beginTransaction(SPISettings(14000000, MSBFIRST, SPI_MODE0));
  digitalWrite(ETH_CS_PIN, LOW);
  SPI.transfer(0x00);
  SPI.transfer(0x2E);   // PHYCFGR
  SPI.transfer(0x04);   // write common reg
  SPI.transfer(value);
  digitalWrite(ETH_CS_PIN, HIGH);
  SPI.endTransaction();
}

void w5500PhySleep() {
  w5500WritePhyCfg(0xB0);   // RST=1, OPMD=1, OPMDC=110 (power-down)
  Serial.println("[PHY] W5500 PHY en power-down");
}

bool waitForLink(unsigned long timeout_ms) {
  unsigned long start = millis();
  while (millis() - start < timeout_ms) {
    if (Ethernet.linkStatus() == LinkON) return true;
    delay(50);
  }
  return false;
}

// ─── Tentatives de connexion ─────────────────────────────────────────────────
bool tryEthernet() {
  Serial.println("[1/3] Tentative Ethernet...");

  SPI.begin();
  pinMode(ETH_CS_PIN, OUTPUT);
  digitalWrite(ETH_CS_PIN, HIGH);
  Ethernet.init(ETH_CS_PIN);

  Serial.println("      Attente link...");
  if (!waitForLink(LINK_UP_WAIT_MS)) {
    Serial.println("      [--] Pas de cable Ethernet");
    w5500PhySleep();
    return false;
  }

  Serial.println("      [OK] Link detecte, DHCP...");
  if (Ethernet.begin(mac) == 0) {
    Serial.println("      [KO] DHCP echec");
    if (Ethernet.hardwareStatus() == EthernetNoHardware) {
      Serial.println("           -> W5500 non detecte");
    }
    w5500PhySleep();
    return false;
  }

  ethUdp.begin(OSC_IN_PORT);
  Serial.print("      [OK] IP : ");
  Serial.println(Ethernet.localIP());
  return true;
}

bool tryWiFi() {
  Serial.println("[2/3] Tentative WiFi (WiFiManager)...");
  WiFiManager wm;
  wm.setConfigPortalTimeout(WIFI_PORTAL_TIMEOUT);
  wm.setConnectTimeout(15);

  if (forceResetPortal) {
    Serial.println("      [!] Triple power-cycle -> reset credentials + portail force");
    wm.resetSettings();
  }

  Serial.println("      Credentials stockes ? AP 'Daddy_controllerSETUP' si echec.");
  if (!wm.autoConnect("Daddy_controllerSETUP")) {
    Serial.println("      [KO] WiFi timeout / portail non complete");
    return false;
  }

  wifiUdp.begin(OSC_IN_PORT);
  Serial.print("      [OK] WiFi connecte, IP : ");
  Serial.println(WiFi.localIP());
  return true;
}

// ─── Triple power-cycle detection ────────────────────────────────────────────
void handleBootCounter() {
  bootPrefs.begin("bootcnt", false);
  int count = bootPrefs.getInt("n", 0) + 1;
  bootPrefs.putInt("n", count);
  Serial.print("[BOOT] count=");
  Serial.println(count);

  if (count >= RESET_BOOT_THRESHOLD) {
    forceResetPortal = true;
    bootPrefs.putInt("n", 0);
    Serial.println("[BOOT] Triple power-cycle detecte !");
  }
  bootPrefs.end();
}

void clearBootCounterIfStable() {
  bootPrefs.begin("bootcnt", false);
  int current = bootPrefs.getInt("n", 0);
  if (current != 0) {
    bootPrefs.putInt("n", 0);
    Serial.println("[BOOT] uptime stable -> compteur reset");
  }
  bootPrefs.end();
}

// ─── Setup ───────────────────────────────────────────────────────────────────
void setup() {
  Serial.begin(115200);
  while (!Serial && millis() < 3000) {}
  Serial.println();
  Serial.println("=== ForceOSCeth ===");

  handleBootCounter();

  // Alim NeoPixel onboard
#ifdef NEOPIXEL_POWER
  pinMode(NEOPIXEL_POWER, OUTPUT);
  digitalWrite(NEOPIXEL_POWER, HIGH);
#endif

  led.begin();
  led.setBrightness(5);
  ledOnboard.begin();
  ledOnboard.setBrightness(5);
  ledWrite(COL_OFF);

  // Flash blanc au boot
  ledWrite(COL_WHITE);
  Serial.println("LED: flash blanc (boot)");
  delay(300);
  ledWrite(COL_OFF);

  // Cascade Eth -> WiFi -> Serial (LED cyan pendant toute la phase reseau)
  ledWrite(COL_CYAN);
  if (tryEthernet()) {
    netMode = NET_ETH;
    Serial.println(">>> Mode : ETHERNET");
  } else if (tryWiFi()) {
    netMode = NET_WIFI;
    Serial.println(">>> Mode : WIFI");
  } else {
    netMode = NET_NONE;
    Serial.println("[3/3] Fallback -> Serial only");
    Serial.println(">>> Mode : SERIAL ONLY");
  }
  ledWrite(COL_OFF);

  Serial.print("     Envoi OSC sur port ");
  Serial.print(OSC_OUT_PORT);
  Serial.println(" (broadcast tant que pas de /ping recu)");
  Serial.print("     Ecoute OSC sur port ");
  Serial.println(OSC_IN_PORT);

  // Init HX711
  const uint8_t datPins[NUM_SCALES] = {LOADCELL1_DAT_PIN, LOADCELL2_DAT_PIN, LOADCELL3_DAT_PIN, LOADCELL4_DAT_PIN};
  const uint8_t clkPins[NUM_SCALES] = {LOADCELL1_CLK_PIN, LOADCELL2_CLK_PIN, LOADCELL3_CLK_PIN, LOADCELL4_CLK_PIN};
  for (int i = 0; i < NUM_SCALES; i++) {
    scales[i].begin(datPins[i], clkPins[i]);
    scales[i].set_scale(CALIBRATION_FACTOR[i]);
    scales[i].tare();
  }

  for (int i = 0; i < MED_SIZE; i++) medBuf[i] = 0.0f;

  ledWrite(COL_OFF);
}

// ─── Loop ────────────────────────────────────────────────────────────────────
void loop() {
  unsigned long now = millis();

  // Effacement du compteur de boot une fois uptime stable
  if (!bootCounterCleared && now > BOOT_GRACE_MS) {
    clearBootCounterIfStable();
    bootCounterCleared = true;
  }

  if (netMode == NET_ETH) Ethernet.maintain();

  checkIncoming();

  // Batterie toutes les 30s
  static bool lastLowBattery = false;
  if (now - lastBatCheck > BAT_CHECK_MS) {
    lastBatCheck = now;
    float v = analogRead(VBAT_PIN) * 3.3f / 4095.0f * 2.0f;
    if (v > 3.0f && v < 4.3f) {
      lowBattery = (v < VBAT_LOW);
    }
    if (lowBattery != lastLowBattery) {
      Serial.printf("Batterie : %.2fV %s\n", v, lowBattery ? "(LOW)" : "(OK)");
      lastLowBattery = lowBattery;
    }
  }

  if (lowBattery && !flashActive) {
    ledSet(COL_RED);
  }

  // Clignotement bleu tant que pas connecte (ni reseau ni serial)
  bool connected = (netMode != NET_NONE) || Serial;
  static unsigned long lastBlueBlink = 0;
  static bool blueBlinkState = false;
  if (!connected && !lowBattery && now - lastBlueBlink > 500) {
    lastBlueBlink = now;
    blueBlinkState = !blueBlinkState;
    ledWrite(blueBlinkState ? COL_BLUE : COL_OFF);
  }

  // Heartbeat vert toutes les 5s si connecte
  if (connected && !lowBattery && now - lastHeartbeat > HEARTBEAT_MS) {
    lastHeartbeat = now;
    Serial.print("LED: flash vert (heartbeat) — mode:");
    switch (netMode) {
      case NET_ETH:  Serial.println("ETH"); break;
      case NET_WIFI: Serial.println("WIFI"); break;
      default:       Serial.println("SERIAL"); break;
    }
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
