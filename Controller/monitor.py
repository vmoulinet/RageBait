"""
monitor.py — Chase Controller Monitor
Écoute les données OSC du contrôleur (port 8000) et envoie /ping toutes les 2s (port 8001).
Lit aussi le Serial USB si disponible.
Affiche : force, stomps, qualité WiFi, mode de connexion (OSC / Serial / aucun).

Dépendances :
    pip install python-osc pyserial
"""

import sys
import time
import socket
import threading
import datetime
import struct
from collections import deque

# ── OSC minimal (pas de pythonosc requis pour l'envoi, mais on l'utilise pour recevoir) ──
try:
    from pythonosc import dispatcher, osc_server, udp_client
    HAS_OSC = True
except ImportError:
    HAS_OSC = False
    print("[WARN] python-osc non installé. Installez avec : pip install python-osc")

try:
    import serial
    import serial.tools.list_ports
    HAS_SERIAL = True
except ImportError:
    HAS_SERIAL = False
    print("[WARN] pyserial non installé. Installez avec : pip install pyserial")

# ── Config ────────────────────────────────────────────────────────────────────
OSC_LISTEN_PORT  = 8000   # port sur lequel on reçoit du contrôleur
OSC_TARGET_PORT  = 8001   # port sur lequel le contrôleur écoute /ping
PING_INTERVAL    = 2.0    # secondes entre chaque /ping
SERIAL_BAUD      = 115200
OSC_TIMEOUT_S    = 5.0    # après combien de secondes sans OSC on considère KO

# ── État partagé ──────────────────────────────────────────────────────────────
state = {
    "force":         0.0,
    "last_stomp":    None,
    "last_osc_time": 0.0,
    "osc_connected": False,
    "serial_connected": False,
    "log":           deque(maxlen=20),
    "pong_received": False,
    "controller_ip": None,
}
lock = threading.Lock()

LOG_FILE = f"chase_log_{datetime.datetime.now().strftime('%Y%m%d_%H%M%S')}.txt"
log_fh = open(LOG_FILE, "w", encoding="utf-8")

def log(msg):
    ts = datetime.datetime.now().strftime("%H:%M:%S.%f")[:-3]
    line = f"[{ts}] {msg}"
    with lock:
        state["log"].append(line)
    log_fh.write(line + "\n")
    log_fh.flush()

# ── OSC receive ───────────────────────────────────────────────────────────────
def on_force(addr, value):
    with lock:
        state["force"] = value
        state["last_osc_time"] = time.time()
        state["osc_connected"] = True

def on_stomp(addr, value):
    msg = f"STOMP  {value:.2f}"
    log(msg)
    with lock:
        state["last_stomp"] = (time.time(), value)
        state["last_osc_time"] = time.time()
        state["osc_connected"] = True

def on_pong(addr, *args):
    with lock:
        state["pong_received"] = True
        state["last_osc_time"] = time.time()
        state["osc_connected"] = True
    log("Pong reçu du contrôleur")

def start_osc_server():
    if not HAS_OSC:
        return
    d = dispatcher.Dispatcher()
    d.map("/force", on_force)
    d.map("/stomp", on_stomp)
    d.map("/pong",  on_pong)
    d.set_default_handler(lambda addr, *a: None)
    try:
        server = osc_server.ThreadingOSCUDPServer(("0.0.0.0", OSC_LISTEN_PORT), d)
        log(f"OSC server sur port {OSC_LISTEN_PORT}")
        server.serve_forever()
    except Exception as e:
        log(f"OSC server erreur : {e}")

# ── Ping thread ───────────────────────────────────────────────────────────────
def ping_loop():
    if not HAS_OSC:
        return
    # Broadcast /ping sur le réseau
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)

    def build_osc_message(address):
        # Encodage OSC minimal
        addr_bytes = address.encode() + b'\x00'
        while len(addr_bytes) % 4:
            addr_bytes += b'\x00'
        type_tag = b',\x00\x00\x00'
        return addr_bytes + type_tag

    msg = build_osc_message("/ping")
    while True:
        try:
            sock.sendto(msg, ("255.255.255.255", OSC_TARGET_PORT))
        except Exception as e:
            log(f"Ping erreur : {e}")
        time.sleep(PING_INTERVAL)

# ── Serial thread ─────────────────────────────────────────────────────────────
serial_port = None

def find_serial():
    if not HAS_SERIAL:
        return None
    ports = list(serial.tools.list_ports.comports())
    for p in ports:
        desc = (p.description or "").lower()
        if "esp32" in desc or "usb" in desc or "serial" in desc or "cp210" in desc or "ch340" in desc:
            return p.device
    if ports:
        return ports[0].device
    return None

def serial_loop():
    global serial_port
    while True:
        port = find_serial()
        if not port:
            with lock:
                state["serial_connected"] = False
            time.sleep(2)
            continue
        try:
            with serial.Serial(port, SERIAL_BAUD, timeout=1) as ser:
                log(f"Serial connecté sur {port}")
                with lock:
                    state["serial_connected"] = True
                while True:
                    line = ser.readline().decode("utf-8", errors="ignore").strip()
                    if not line:
                        continue
                    if line.startswith("FORCE/"):
                        try:
                            val = float(line.split("/", 1)[1])
                            with lock:
                                state["force"] = val
                                state["last_osc_time"] = time.time()
                        except ValueError:
                            pass
                    elif line.startswith("STOMP/"):
                        try:
                            val = float(line.split("/", 1)[1])
                        except ValueError:
                            val = 0.0
                        log(f"STOMP  {val:.2f}")
                        with lock:
                            state["last_stomp"] = (time.time(), val)
                            state["last_osc_time"] = time.time()
                    # On logue tout en fichier (sauf le flux FORCE/ trop verbeux)
                    if not line.startswith("FORCE/"):
                        log_fh.write(f"[SERIAL] {line}\n")
                        log_fh.flush()
        except Exception as e:
            with lock:
                state["serial_connected"] = False
            time.sleep(2)

# ── Affichage ─────────────────────────────────────────────────────────────────
def rssi_bars(rssi):
    """Convertit RSSI en barres (ESP32 ne l'expose pas via OSC, estimation via pong latency)."""
    # On n'a pas le RSSI directement — on affiche juste connecté/pas connecté
    return "●●●●" if rssi else "○○○○"

def clear():
    print("\033[H\033[J", end="")

def display_loop():
    while True:
        time.sleep(0.2)
        with lock:
            force       = state["force"]
            osc_ok      = state["osc_connected"]
            ser_ok      = state["serial_connected"]
            last_stomp  = state["last_stomp"]
            logs        = list(state["log"])
            last_osc    = state["last_osc_time"]

        # Vérifier timeout OSC
        if osc_ok and (time.time() - last_osc > OSC_TIMEOUT_S):
            with lock:
                state["osc_connected"] = False
            osc_ok = False

        # Mode connexion
        if osc_ok:
            mode = "\033[92m● OSC WiFi\033[0m"
        elif ser_ok:
            mode = "\033[93m● Serial USB\033[0m"
        else:
            mode = "\033[91m○ Aucune connexion\033[0m"

        # Barre de force
        bar_len = 30
        bar_val = min(int(force / 150 * bar_len), bar_len)
        bar = "█" * bar_val + "░" * (bar_len - bar_val)

        stomp_str = "—"
        if last_stomp:
            age = time.time() - last_stomp[0]
            stomp_str = f"{last_stomp[1]:.1f} kg  ({age:.1f}s)"

        clear()
        print("╔══════════════════════════════════════╗")
        print("║     Chase Controller Monitor         ║")
        print("╠══════════════════════════════════════╣")
        print(f"║  Mode    : {mode:<30}║")
        print(f"║  Force   : {force:6.1f} kg                    ║")
        print(f"║  [{bar}] ║")
        print(f"║  Stomp   : {stomp_str:<28}║")
        print(f"║  Log     : {LOG_FILE:<28}║")
        print("╠══════════════════════════════════════╣")
        print("║  Derniers événements :               ║")
        recent = logs[-8:] if len(logs) >= 8 else logs
        for l in recent:
            print(f"║  {l[-38:]:<38}║")
        # Remplir si moins de 8 lignes
        for _ in range(8 - len(recent)):
            print(f"║  {'':38}║")
        print("╚══════════════════════════════════════╝")
        print(f"  Ctrl+C pour quitter  |  Log : {LOG_FILE}")

# ── Main ──────────────────────────────────────────────────────────────────────
if __name__ == "__main__":
    log("Monitor démarré")

    threads = [
        threading.Thread(target=start_osc_server, daemon=True),
        threading.Thread(target=ping_loop,        daemon=True),
        threading.Thread(target=serial_loop,      daemon=True),
    ]
    for t in threads:
        t.start()

    try:
        display_loop()
    except KeyboardInterrupt:
        log("Monitor arrêté")
        log_fh.close()
        print("\nAu revoir.")
