using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using extOSC;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

public class IOManager : MonoBehaviour
{
	[Header("OSC Settings")]
	public string ListenAddress = "0.0.0.0";
	public int ListenPort = 8000;
	public int PingPort = 8001;
	public string PingHost = "255.255.255.255";
	public string StompAddress = "/stomp";
	public float DefaultDebugStompForce = 1f;

	[Header("Sniffer")]
	public string SniffAddress = "/*";

	[Header("UI Log")]
	public TMP_Text LogText;
	public int MaxLogLines = 20;

	[Header("UI Status")]
	public TMP_Text ControllerStatusText;
	public TMP_Text WifiQualityText;
	public Color ConnectedColor = Color.green;
	public Color DisconnectedColor = Color.red;
	public Color UnknownColor = Color.gray;

	[Header("Ping")]
	public bool EnablePing = true;
	public float PingInterval = 5f;
	public string PingAddress = "/ping";
	public float PongTimeout = 8f;

	[Header("WiFi")]
	public bool EnableWifiPolling = true;
	public float WifiPollInterval = 2f;

	[Header("Debug")]
	public bool DebugLogPackets = true;
	public int MaxStoredPackets = 32;
	public bool LogSentMessages = true;

	OSCReceiver receiver;
	OSCTransmitter transmitter;
	string last_pong_summary = "";
	SimulationManager simulation_manager;
	float last_stomp_force = 0f;
	float last_stomp_time = -999f;

	readonly List<string> recent_packets = new List<string>();
	readonly Queue<string> log_lines = new Queue<string>();

	int total_packets_received = 0;
	string last_packet_summary = "";
	float ping_timer = 0f;
	float wifi_poll_timer = 0f;
	float last_pong_time = -999f;
	bool is_controller_connected = false;
	int last_wifi_rssi = int.MinValue;
	string last_wifi_bars = "....";
	string last_wifi_summary = "wifi n/a";

	public int TotalPacketsReceived
	{
		get
		{
			return total_packets_received;
		}
	}

	public string LastPacketSummary
	{
		get
		{
			return last_packet_summary;
		}
	}

	public IReadOnlyList<string> RecentPackets
	{
		get
		{
			return recent_packets;
		}
	}

	public bool IsControllerConnected
	{
		get
		{
			return is_controller_connected;
		}
	}

	public string LastPongSummary
	{
		get
		{
			return last_pong_summary;
		}
	}

	public int LastWifiRssi
	{
		get
		{
			return last_wifi_rssi;
		}
	}

	public string LastWifiBars
	{
		get
		{
			return last_wifi_bars;
		}
	}

	public float LastStompForce
	{
		get
		{
			return last_stomp_force;
		}
	}

	public float LastStompTime
	{
		get
		{
			return last_stomp_time;
		}
	}

	public void Initialize(SimulationManager sim)
	{
		simulation_manager = sim;
		InitializeReceiver();
		InitializeTransmitter();
		ping_timer = PingInterval;
		wifi_poll_timer = 0f;
		RefreshLogText();
		RefreshControllerStatusText();
		RefreshWifiQualityText();
	}

	void InitializeTransmitter()
	{
		if (transmitter != null)
			return;

		transmitter = GetComponent<OSCTransmitter>();
		if (transmitter == null)
			transmitter = gameObject.AddComponent<OSCTransmitter>();

		transmitter.RemoteHost = string.IsNullOrWhiteSpace(PingHost) ? "255.255.255.255" : PingHost.Trim();
		transmitter.RemotePort = PingPort;
	}

	void InitializeReceiver()
	{
		if (receiver != null)
			return;

		receiver = GetComponent<OSCReceiver>();
		if (receiver == null)
			receiver = gameObject.AddComponent<OSCReceiver>();

		receiver.LocalPort = ListenPort;
		receiver.ClearBinds();

		string sniff_address = string.IsNullOrWhiteSpace(SniffAddress) ? "/*" : SniffAddress.Trim();
		receiver.Bind(sniff_address, OnOscMessageReceived);

		Debug.Log(
			"[io_manager] listening | listen_address=" + ListenAddress +
			" | listen_port=" + ListenPort +
			" | ping_port=" + PingPort +
			" | ping_host=" + (string.IsNullOrWhiteSpace(PingHost) ? "255.255.255.255" : PingHost.Trim()) +
			" | stomp_address=" + StompAddress +
			" | sniff_address=" + sniff_address
		);
	}

	void OnDestroy()
	{
		if (receiver != null)
			receiver.ClearBinds();

		transmitter = null;
	}

	void Update()
	{
		UpdatePing();
		UpdateControllerConnectionState();
		UpdateWifiQuality();
	}

	void UpdatePing()
	{
		if (!EnablePing)
			return;

		if (PingInterval <= 0f)
			return;

		if (transmitter == null)
			return;

		ping_timer -= Time.unscaledDeltaTime;
		if (ping_timer > 0f)
			return;

		ping_timer = PingInterval;
		SendPing();
	}

	void SendPing()
	{
		OSCMessage ping_message = new OSCMessage(PingAddress);
		transmitter.Send(ping_message);

		if (LogSentMessages)
		{
			AddLog(
				"[sent] " + PingAddress,
				"ping -> " + (string.IsNullOrWhiteSpace(PingHost) ? "255.255.255.255" : PingHost.Trim()) + ":" + PingPort
			);
		}
	}

	void UpdateControllerConnectionState()
	{
		bool was_connected = is_controller_connected;
		bool has_recent_pong = Time.unscaledTime - last_pong_time <= PongTimeout;
		is_controller_connected = has_recent_pong;

		if (was_connected != is_controller_connected)
		{
			if (is_controller_connected)
				AddLog("[state] controller", "connected");
			else
				AddLog("[state] controller", "disconnected");

			RefreshControllerStatusText();
		}
	}

	void UpdateWifiQuality()
	{
		if (!EnableWifiPolling)
			return;

		if (WifiPollInterval <= 0f)
			return;

		wifi_poll_timer -= Time.unscaledDeltaTime;
		if (wifi_poll_timer > 0f)
			return;

		wifi_poll_timer = WifiPollInterval;

		int rssi;
		if (TryGetDesktopWifiRssi(out rssi))
		{
			last_wifi_rssi = rssi;
			last_wifi_bars = RssiToAsciiBars(rssi);
			last_wifi_summary = last_wifi_bars + " (" + rssi + " dBm)";
		}
		else
		{
			last_wifi_rssi = int.MinValue;
			last_wifi_bars = "....";
			last_wifi_summary = "wifi n/a";
		}

		RefreshWifiQualityText();
	}

	bool TryGetDesktopWifiRssi(out int rssi)
	{
		rssi = int.MinValue;

		RuntimePlatform platform = Application.platform;
		if (platform != RuntimePlatform.OSXEditor && platform != RuntimePlatform.OSXPlayer)
			return false;

		const string airport_path = "/System/Library/PrivateFrameworks/Apple80211.framework/Versions/Current/Resources/airport";

		try
		{
			ProcessStartInfo start_info = new ProcessStartInfo();
			start_info.FileName = airport_path;
			start_info.Arguments = "-I";
			start_info.UseShellExecute = false;
			start_info.RedirectStandardOutput = true;
			start_info.RedirectStandardError = true;
			start_info.CreateNoWindow = true;

			using (Process process = Process.Start(start_info))
			{
				if (process == null)
					return false;

				string output = process.StandardOutput.ReadToEnd();
				process.WaitForExit(1000);

				if (string.IsNullOrWhiteSpace(output))
					return false;

				string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
				for (int i = 0; i < lines.Length; i++)
				{
					string line = lines[i].Trim();
					if (!line.StartsWith("agrCtlRSSI:"))
						continue;

					string value_text = line.Substring("agrCtlRSSI:".Length).Trim();
					if (int.TryParse(value_text, out rssi))
						return true;
				}
			}
		}
		catch (Exception)
		{
			return false;
		}

		return false;
	}

	string RssiToAsciiBars(int rssi)
	{
		if (rssi >= -55)
			return "||||";
		if (rssi >= -67)
			return "|||.";
		if (rssi >= -75)
			return "||..";
		if (rssi >= -85)
			return "|...";
		return "....";
	}

	void RefreshControllerStatusText()
	{
		if (ControllerStatusText == null)
			return;

		ControllerStatusText.text = is_controller_connected ? "Controller Status Connected" : "Controller Status Disconnected";
		ControllerStatusText.color = is_controller_connected ? ConnectedColor : DisconnectedColor;
	}

	void RefreshWifiQualityText()
	{
		if (WifiQualityText == null)
			return;

		WifiQualityText.text = last_wifi_summary;
		WifiQualityText.color = last_wifi_rssi == int.MinValue ? UnknownColor : ConnectedColor;
	}

	void AddLog(string address, string message)
	{
		string timestamp = DateTime.Now.ToString("HH:mm:ss");
		string prefix = "> " + timestamp + " " + address + ": ";
		string indent = "                    ";
		string line = prefix + message.Replace("\n", "\n" + indent);

		log_lines.Enqueue(line);

		while (log_lines.Count > Mathf.Max(1, MaxLogLines))
			log_lines.Dequeue();

		RefreshLogText();
	}

	void RefreshLogText()
	{
		if (LogText == null)
			return;

		string[] lines = log_lines.ToArray();
		Array.Reverse(lines);
		LogText.text = string.Join("\n", lines);
	}

	void TryHandleStomp(OSCMessage message)
	{
		if (message == null)
			return;

		if (string.IsNullOrWhiteSpace(StompAddress))
			return;

		if (message.Address != StompAddress)
			return;

		float stomp_force = DefaultDebugStompForce;

		if (message.Values != null && message.Values.Count > 0)
		{
			OSCValue first_value = message.Values[0];
			if (first_value != null)
			{
				switch (first_value.Type)
				{
					case OSCValueType.Float:
						stomp_force = first_value.FloatValue;
						break;

					case OSCValueType.Double:
						stomp_force = (float)first_value.DoubleValue;
						break;

					case OSCValueType.Int:
						stomp_force = first_value.IntValue;
						break;

					case OSCValueType.Long:
						stomp_force = first_value.LongValue;
						break;
				}
			}
		}

		last_stomp_force = stomp_force;
		last_stomp_time = Time.unscaledTime;

		AddLog("[state] stomp", "force=" + stomp_force.ToString("F3"));

		if (simulation_manager != null && simulation_manager.EventManager != null)
			simulation_manager.EventManager.NotifyStomp(stomp_force, "osc");
	}

	void OnOscMessageReceived(OSCMessage message)
	{
		total_packets_received++;

		string packet_summary = BuildPacketSummary(message);
		last_packet_summary = packet_summary;
		recent_packets.Add(packet_summary);

		if (recent_packets.Count > Mathf.Max(1, MaxStoredPackets))
			recent_packets.RemoveAt(0);

		if (DebugLogPackets)
			Debug.Log("[osc] " + packet_summary);
		TryHandleStomp(message);

		if (message != null && message.Address == "/pong")
		{
			last_pong_time = Time.unscaledTime;
			last_pong_summary = packet_summary;
			UpdateControllerConnectionState();
			AddLog("[recv] /pong", BuildPacketPayloadSummary(message));
		}
		else
		{
			string address = message != null ? message.Address : "<null>";
			string payload = BuildPacketPayloadSummary(message);
			AddLog("[recv] " + address, payload);
		}
	}

	string BuildPacketSummary(OSCMessage message)
	{
		StringBuilder builder = new StringBuilder();
		builder.Append("#");
		builder.Append(total_packets_received);
		builder.Append(" | ");

		if (message != null)
			builder.Append(message.Address);
		else
			builder.Append("<null>");

		builder.Append(" | ");

		if (message == null || message.Values == null || message.Values.Count == 0)
		{
			builder.Append("<no_args>");
			return builder.ToString();
		}

		for (int i = 0; i < message.Values.Count; i++)
		{
			OSCValue value = message.Values[i];
			builder.Append(OscValueToString(value));

			if (i < message.Values.Count - 1)
				builder.Append(", ");
		}

		return builder.ToString();
	}

	string BuildPacketPayloadSummary(OSCMessage message)
	{
		if (message == null || message.Values == null || message.Values.Count == 0)
			return "<no_args>";

		StringBuilder builder = new StringBuilder();

		for (int i = 0; i < message.Values.Count; i++)
		{
			OSCValue value = message.Values[i];
			builder.Append(OscValueToString(value));

			if (i < message.Values.Count - 1)
				builder.Append(", ");
		}

		return builder.ToString();
	}

	string OscValueToString(OSCValue value)
	{
		if (value == null)
			return "null";

		switch (value.Type)
		{
			case OSCValueType.Int:
				return value.IntValue.ToString();

			case OSCValueType.Float:
				return value.FloatValue.ToString("F3");

			case OSCValueType.String:
				return value.StringValue;

			case OSCValueType.Long:
				return value.LongValue.ToString();

			case OSCValueType.Double:
				return value.DoubleValue.ToString("F3");

			case OSCValueType.True:
				return "true";

			case OSCValueType.False:
				return "false";

			case OSCValueType.Null:
				return "null";

			case OSCValueType.Impulse:
				return "impulse";
		}

		return value.ToString();
	}
}