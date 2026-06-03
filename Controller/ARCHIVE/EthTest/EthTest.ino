/*
  EthTest - W5500 + bouton -> OSC /stomp
  Carte : NodeMCU-32S (ESP32 classique)

  Cascade au boot : Ethernet -> WiFi -> Serial only
    1. Tentative Ethernet (W5500) : attente link 2s
       - Link OK -> DHCP + OSC, W5500 reste allume. Fin.
       - Pas de link -> PHY power-down et on passe au WiFi.
    2. Tentative WiFi via WiFiManager (portail "Daddy_controllerSETUP") : timeout 60s
       - OK -> OSC sur WiFi. Fin.
       - Echec -> on passe en Serial only.
    3. Serial only : les stomps sont juste imprimes sur le Serial.

  Pas de re-check periodique du link. Pour changer de mode de connectivite,
  redemarrer l'ESP (power cycle ou bouton EN).

  Cablage :
    W5500 (VSPI) : MOSI=23, MISO=19, SCLK=18, CS=5
      - RST tiré a 3.3V (obligatoire sur modules clones)
      - INT non connecte
      - VCC=3.3V, GND=GND (MOSFET 2N7000 bypassé, GPIO17 prevu pour futur
        MOSFET logic-level)
    Bouton : GPIO16 <-> GND (pull-up interne)

  OSC (meme convention que ForceOSC) :
    - out port 8000, in port 8001
    - /ping recu -> repond /pong et memorise l'IP source (unityIP)
    - tant que pas de /ping -> broadcast 255.255.255.255
    - /stomp float (toujours 1.0, pas de force sur un bouton)

  Bibliotheques :
    - Ethernet (Arduino)
    - OSC by CNMAT / Adrian Freed : https://github.com/CNMAT/OSC
    - WiFiManager by tzapu : https://github.com/tzapu/WiFiManager
*/

#include <SPI.h>
#include <Ethernet.h>
#include <EthernetUdp.h>
#include <WiFi.h>
#include <WiFiUdp.h>
#include <WiFiManager.h>
#include <OSCMessage.h>
#include <Preferences.h>

// ─── Pins ────────────────────────────────────────────────────────────────────
#define ETH_POWER_PIN 17   // Gate MOSFET (futur) - HIGH = W5500 alimente
#define ETH_CS_PIN    5    // VSPI CS0
#define BUTTON_PIN    16   // bouton vers GND, pull-up interne

// ─── OSC ─────────────────────────────────────────────────────────────────────
const unsigned int OSC_OUT_PORT = 8000;
const unsigned int OSC_IN_PORT  = 8001;

byte mac[] = { 0xDE, 0xAD, 0xBE, 0xEF, 0xFE, 0x01 };

EthernetUDP ethUdp;
WiFiUDP     wifiUdp;
IPAddress   broadcastIP(255, 255, 255, 255);
IPAddress   unityIP(0, 0, 0, 0);

enum NetMode { NET_NONE, NET_ETH, NET_WIFI };
NetMode netMode = NET_NONE;

// ─── Link wait ───────────────────────────────────────────────────────────────
const unsigned long LINK_UP_WAIT_MS     = 2000;
const unsigned long WIFI_PORTAL_TIMEOUT = 60;   // secondes (timeout portail captif)

// ─── Reset trigger (triple power-cycle) ──────────────────────────────────────
// 3 reboots rapides (<10s d'uptime) -> reset credentials WiFi + portail force
const unsigned long BOOT_GRACE_MS        = 10000;  // fenetre pour qu'un reboot compte
const int           RESET_BOOT_THRESHOLD = 3;
Preferences bootPrefs;
bool forceResetPortal = false;

// ─── Bouton (debounce) ───────────────────────────────────────────────────────
const unsigned long DEBOUNCE_MS       = 30;
const unsigned long STOMP_COOLDOWN_MS = 300;

bool          lastStableState = HIGH;
bool          lastReadState   = HIGH;
unsigned long lastChangeTime  = 0;
unsigned long lastStompTime   = 0;

// ─── OSC helpers ─────────────────────────────────────────────────────────────
void sendOSC(const char* address, float value) {
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
  Serial.print("[OSC] /ping recu depuis ");
  Serial.println(unityIP);
  OSCMessage pong("/pong");
  pong.add((float)1.0);
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
    uint16_t  port = ethUdp.remotePort();
    Serial.print("[UDP] paquet recu de ");
    Serial.print(from);
    Serial.print(":");
    Serial.print(port);
    Serial.print(" (");
    Serial.print(size);
    Serial.println(" bytes)");
    OSCMessage msg;
    while (size--) msg.fill(ethUdp.read());
    if (!msg.hasError() && msg.fullMatch("/ping")) handlePing(from);
  } else if (netMode == NET_WIFI) {
    int size = wifiUdp.parsePacket();
    if (size <= 0) return;
    IPAddress from = wifiUdp.remoteIP();
    uint16_t  port = wifiUdp.remotePort();
    Serial.print("[UDP] paquet recu de ");
    Serial.print(from);
    Serial.print(":");
    Serial.print(port);
    Serial.print(" (");
    Serial.print(size);
    Serial.println(" bytes)");
    OSCMessage msg;
    while (size--) msg.fill(wifiUdp.read());
    if (!msg.hasError() && msg.fullMatch("/ping")) handlePing(from);
  }
}

// ─── W5500 PHY power ─────────────────────────────────────────────────────────
void powerOnW5500() {
  pinMode(ETH_POWER_PIN, OUTPUT);
  digitalWrite(ETH_POWER_PIN, HIGH);
  delay(100);
}

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

// PHY en power-down : RST=1, OPMD=1, OPMDC=110
void w5500PhySleep() {
  w5500WritePhyCfg(0xB0);
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
  powerOnW5500();

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
    Serial.println("      [!] Triple power-cycle detecte -> reset credentials + portail force");
    wm.resetSettings();
  }

  // autoConnect :
  //   - tente de se reconnecter aux credentials stockes en flash
  //   - si echec, demarre un AP "Daddy_controllerSETUP" avec portail captif
  //   - si le user ne configure pas avant WIFI_PORTAL_TIMEOUT -> false
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
// Au boot on incremente un compteur en NVS. Si >= threshold -> trigger reset.
// Une tache schedule remet le compteur a 0 apres BOOT_GRACE_MS (un seul reboot
// "stable" efface l'historique).
void handleBootCounter() {
  bootPrefs.begin("bootcnt", false);
  int count = bootPrefs.getInt("n", 0) + 1;
  bootPrefs.putInt("n", count);
  Serial.print("[BOOT] count=");
  Serial.println(count);

  if (count >= RESET_BOOT_THRESHOLD) {
    forceResetPortal = true;
    bootPrefs.putInt("n", 0);   // on consomme le trigger
    Serial.println("[BOOT] Triple power-cycle detecte !");
  }
  bootPrefs.end();
}

void clearBootCounterIfStable() {
  // Appele une fois que l'uptime depasse BOOT_GRACE_MS
  bootPrefs.begin("bootcnt", false);
  int current = bootPrefs.getInt("n", 0);
  if (current != 0) {
    bootPrefs.putInt("n", 0);
    Serial.println("[BOOT] uptime stable -> compteur reset");
  }
  bootPrefs.end();
}

bool bootCounterCleared = false;

// ─── Setup ───────────────────────────────────────────────────────────────────
void setup() {
  Serial.begin(115200);
  while (!Serial && millis() < 3000) {}
  Serial.println();
  Serial.println("=== EthTest W5500 + bouton ===");

  handleBootCounter();

  pinMode(BUTTON_PIN, INPUT_PULLUP);

  if (tryEthernet()) {
    netMode = NET_ETH;
    Serial.println(">>> Mode : ETHERNET");
  } else if (tryWiFi()) {
    netMode = NET_WIFI;
    Serial.println(">>> Mode : WIFI");
  } else {
    netMode = NET_NONE;
    Serial.println("[3/3] Fallback -> Serial only (pas de reseau)");
    Serial.println(">>> Mode : SERIAL ONLY");
  }

  Serial.print("     Envoi OSC sur port ");
  Serial.print(OSC_OUT_PORT);
  Serial.println(" (broadcast 255.255.255.255 tant que pas de /ping)");
  Serial.print("     Ecoute OSC sur port ");
  Serial.println(OSC_IN_PORT);
}

// ─── Loop ────────────────────────────────────────────────────────────────────
void loop() {
  unsigned long now = millis();

  // Une fois le boot stable, on efface le compteur de power-cycles
  if (!bootCounterCleared && now > BOOT_GRACE_MS) {
    clearBootCounterIfStable();
    bootCounterCleared = true;
  }

  if (netMode == NET_ETH) Ethernet.maintain();

  checkIncoming();

  // Bouton avec debounce
  bool reading = digitalRead(BUTTON_PIN);
  if (reading != lastReadState) {
    lastChangeTime = now;
    lastReadState  = reading;
  }
  if ((now - lastChangeTime) > DEBOUNCE_MS && reading != lastStableState) {
    lastStableState = reading;
    if (lastStableState == LOW && (now - lastStompTime > STOMP_COOLDOWN_MS)) {
      lastStompTime = now;
      Serial.println(">>> STOMP (bouton) -> /stomp 1.0");
      sendOSC("/stomp", 1.0f);
    }
  }
}
