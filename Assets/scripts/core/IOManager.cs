using System.Collections.Generic;
using System.Text;
using extOSC;
using UnityEngine;

public class IOManager : MonoBehaviour
{
	[Header("OSC Settings")]
	public string ListenAddress = "0.0.0.0";
	public int ListenPort = 8000;
	public int PingPort = 8001;

	[Header("Sniffer")]
	public string SniffAddress = "/*";

	[Header("Debug")]
	public bool DebugLogPackets = true;
	public int MaxStoredPackets = 32;

	OSCReceiver receiver;

	readonly List<string> recent_packets = new List<string>();
	int total_packets_received = 0;
	string last_packet_summary = "";

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

	public void Initialize(SimulationManager sim)
	{
		InitializeReceiver();
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
			" | sniff_address=" + sniff_address
		);
	}

	void OnDestroy()
	{
		if (receiver != null)
			receiver.ClearBinds();
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