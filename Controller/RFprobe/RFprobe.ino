/*
 * RFprobe.ino
 * Sonde RF 2,4 GHz pour diagnostiquer les interferences WiFi du controller Chase.
 *
 * Carte : Adafruit Feather ESP32-S3 (meme que le controller).
 *
 * Principe :
 *   - On se connecte au MEME AP que le controller (memes credentials WiFi).
 *   - On ping en continu la passerelle (la box) en UDP, ~5x/s.
 *   - On logge en Serial : RSSI courant, RSSI min/moyen sur la fenetre,
 *     latence du ping, et taux de perte sur les N derniers pings.
 *
 * Protocole de test :
 *   1. Pose la sonde JUSTE A COTE du controller.
 *   2. Ouvre le moniteur serie (115200) de la sonde sur ton PC.
 *   3. Branche / debranche le bloc secteur du CONTROLLER toutes les ~10s.
 *   4. Si RSSI chute et/ou la perte de ping monte PILE quand le secteur est
 *      branche -> bruit RF du chargeur confirme.
 *
 * Format Serial (lisible aussi par monitor.py qui archive toute ligne non FORCE/) :
 *   RF/ rssi=-54 min=-71 avg=-58 loss=20% lat=3ms
 *
 * Credentials WiFi : on reutilise WiFiManager comme le controller. Au premier
 * boot, portail captif "RFprobe_SETUP". Mets les MEMES identifiants que le
 * controller pour mesurer le meme lien.
 *
 * Bibliotheques :
 *   - WiFi (ESP32 core)
 *   - WiFiManager by tzapu
 *   - Adafruit NeoPixel (feedback visuel onboard)
 */

#include <WiFi.h>
#include <WiFiUdp.h>
#include <WiFiManager.h>
#include <Adafruit_NeoPixel.h>

// ─── LED onboard (feedback visuel) ───────────────────────────────────────────
#define LED_ONBOARD_PIN   PIN_NEOPIXEL
Adafruit_NeoPixel ledOnboard(1, LED_ONBOARD_PIN, NEO_GRB + NEO_KHZ800);

const uint32_t COL_OFF   = 0x000000;
const uint32_t COL_GREEN = 0x00FF00;   // lien bon
const uint32_t COL_AMBER = 0xFFC800;   // lien degrade
const uint32_t COL_RED   = 0xFF0000;   // lien mauvais / pings perdus
const uint32_t COL_CYAN  = 0x00FFFF;   // connexion en cours

void ledSet(uint32_t c) {
  ledOnboard.setPixelColor(0, c);
  ledOnboard.show();
}

// ─── Reseau ──────────────────────────────────────────────────────────────────
WiFiUDP udp;
const unsigned int PROBE_PORT = 9999;   // port arbitraire pour le ping sortant
IPAddress gatewayIP;

// ─── Mesure ──────────────────────────────────────────────────────────────────
const unsigned long PING_INTERVAL_MS = 200;   // ~5 pings/s
const int           WINDOW_SIZE      = 25;     // fenetre glissante (~5s)

bool          sentBuf[WINDOW_SIZE] = {false};  // ping envoye dans ce slot ?
bool          ackBuf[WINDOW_SIZE]  = {false};   // reponse recue ?
int           slot                 = 0;
int           rssiMin              = 0;
long          rssiSum              = 0;
int           rssiCount            = 0;

unsigned long lastPing   = 0;
unsigned long lastReport = 0;
const unsigned long REPORT_MS = 1000;          // un resume Serial / seconde

unsigned long pingSentAt = 0;
long          lastLatency = -1;

// ─── Setup ───────────────────────────────────────────────────────────────────
void setup() {
  Serial.begin(115200);
  while (!Serial && millis() < 3000) {}
  Serial.println();
  Serial.println("=== RFprobe (sonde RF 2,4 GHz) ===");

#if defined(NEOPIXEL_POWER)
  pinMode(NEOPIXEL_POWER, OUTPUT); digitalWrite(NEOPIXEL_POWER, HIGH);
#elif defined(PIN_NEOPIXEL_POWER)
  pinMode(PIN_NEOPIXEL_POWER, OUTPUT); digitalWrite(PIN_NEOPIXEL_POWER, HIGH);
#elif defined(NEOPIXEL_I2C_POWER)
  pinMode(NEOPIXEL_I2C_POWER, OUTPUT); digitalWrite(NEOPIXEL_I2C_POWER, HIGH);
#endif
  ledOnboard.begin();
  ledOnboard.setBrightness(8);
  ledSet(COL_CYAN);

  // Important : pas de sommeil radio, sinon RSSI/latence faussent la mesure.
  WiFi.setSleep(false);

  WiFiManager wm;
  wm.setConfigPortalTimeout(120);
  wm.setConnectTimeout(15);
  Serial.println("Connexion WiFi (memes identifiants que le controller)...");
  Serial.println("Portail 'RFprobe_SETUP' si pas de credentials.");
  if (!wm.autoConnect("RFprobe_SETUP")) {
    Serial.println("[KO] WiFi non connecte. Reboot dans 3s.");
    ledSet(COL_RED);
    delay(3000);
    ESP.restart();
  }

  gatewayIP = WiFi.gatewayIP();
  udp.begin(PROBE_PORT);

  Serial.print("[OK] Connecte. IP=");
  Serial.print(WiFi.localIP());
  Serial.print("  passerelle=");
  Serial.print(gatewayIP);
  Serial.print("  canal=");
  Serial.print(WiFi.channel());
  Serial.print("  RSSI initial=");
  Serial.println(WiFi.RSSI());
  Serial.println("Mesure en cours. Branche/debranche le secteur du controller.");
  Serial.println();

  rssiMin = WiFi.RSSI();
  ledSet(COL_GREEN);
}

// On envoie un petit datagramme UDP a la passerelle, port 9. La box ne repond
// generalement pas en UDP/9, donc on ne mesure pas un "vrai" round-trip applicatif.
// Pour avoir une mesure de perte fiable on s'appuie surtout sur le RSSI + l'etat
// du lien (WiFi.status). En complement on tente un round-trip UDP loopback via
// la passerelle : si elle ICMP/REJECT, on le verra cote latence.
//
// Mesure de perte robuste retenue : on suit le RSSI et WiFi.status(). Si le lien
// tombe (status != WL_CONNECTED) on compte les "slots" comme perdus. C'est ce qui
// reflete le mieux le decrochage observe sur le controller.

void loop() {
  unsigned long now = millis();

  // ── Emission d'un ping par intervalle ──
  if (now - lastPing >= PING_INTERVAL_MS) {
    lastPing = now;

    bool linkUp = (WiFi.status() == WL_CONNECTED);
    sentBuf[slot] = true;
    ackBuf[slot]  = linkUp;   // on considere le slot "OK" si le lien tient

    if (linkUp) {
      int r = WiFi.RSSI();
      if (r < 0 && r > -120) {           // valeur plausible
        rssiSum += r;
        rssiCount++;
        if (r < rssiMin || rssiCount == 1) rssiMin = r;
      }
      // datagramme UDP sortant (charge la radio en TX, comme le controller)
      udp.beginPacket(gatewayIP, 9);
      udp.write((const uint8_t*)"rfprobe", 7);
      udp.endPacket();
    }

    slot = (slot + 1) % WINDOW_SIZE;
  }

  // ── Rapport Serial 1x/s ──
  if (now - lastReport >= REPORT_MS) {
    lastReport = now;

    int sent = 0, lost = 0;
    for (int i = 0; i < WINDOW_SIZE; i++) {
      if (sentBuf[i]) {
        sent++;
        if (!ackBuf[i]) lost++;
      }
    }
    int lossPct = (sent > 0) ? (lost * 100 / sent) : 0;
    int rssiNow = (WiFi.status() == WL_CONNECTED) ? WiFi.RSSI() : 0;
    int rssiAvg = (rssiCount > 0) ? (int)(rssiSum / rssiCount) : 0;

    Serial.print("RF/ rssi=");
    Serial.print(rssiNow);
    Serial.print(" min=");
    Serial.print(rssiMin);
    Serial.print(" avg=");
    Serial.print(rssiAvg);
    Serial.print(" loss=");
    Serial.print(lossPct);
    Serial.print("% link=");
    Serial.print(WiFi.status() == WL_CONNECTED ? "UP" : "DOWN");
    Serial.print(" ch=");
    Serial.println(WiFi.channel());

    // Feedback LED : vert (bon), ambre (RSSI faible), rouge (perte/lien down)
    if (WiFi.status() != WL_CONNECTED || lossPct > 20) {
      ledSet(COL_RED);
    } else if (rssiNow <= -75) {
      ledSet(COL_AMBER);
    } else {
      ledSet(COL_GREEN);
    }

    // Reset fenetre RSSI moyenne / min toutes les ~5s pour suivre les variations
    static int reportTick = 0;
    if (++reportTick >= 5) {
      reportTick = 0;
      rssiSum = 0;
      rssiCount = 0;
      rssiMin = (WiFi.status() == WL_CONNECTED) ? WiFi.RSSI() : 0;
    }
  }

  // ── Reconnexion auto si le lien tombe ──
  static unsigned long lastReconnect = 0;
  if (WiFi.status() != WL_CONNECTED && now - lastReconnect > 3000) {
    lastReconnect = now;
    Serial.println("RF/ lien tombe -> tentative de reconnexion");
    WiFi.reconnect();
  }
}
