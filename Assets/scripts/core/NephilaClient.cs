using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// Minimal client for the Nephila public API (https://nephila.app).
// Used by Chase to run a server-side "move" that generates a breakup letter
// from the redacted words of the daddy letter. See BreakupLetterService.
//
// The API key (Nephila) and the OpenRouter key live in a config file that is
// kept OUT of git (StreamingAssets/nephila_config.json, gitignored). A
// versioned nephila_config.example.json documents the expected shape.
public class NephilaClient : MonoBehaviour
{
	[Serializable]
	public class Config
	{
		public string apiKey = "";          // Nephila key, starts with nph_
		public string collectionId = "";    // collection the move is attached to
		public string breakupMoveId = "";   // the "Breakup Letter Generator" move id
		public string openRouterKey = "";   // sk-or-... passed to the move at run time
	}

	[Header("Config")]
	[Tooltip("File under StreamingAssets, kept out of git. Holds the API keys and ids.")]
	public string ConfigFileName = "nephila_config.json";

	[Header("Endpoint")]
	public string BaseUrl = "https://nephila.app";
	[Tooltip("Hard timeout for a move run (LLM calls can take several seconds).")]
	public int RequestTimeoutSeconds = 60;

	[Header("Debug")]
	public bool DebugLog = false;

	Config config;
	bool config_loaded = false;

	public bool IsConfigured
	{
		get
		{
			EnsureConfig();
			return config != null
				&& !string.IsNullOrEmpty(config.apiKey)
				&& !string.IsNullOrEmpty(config.collectionId)
				&& !string.IsNullOrEmpty(config.breakupMoveId);
		}
	}

	void Awake()
	{
		EnsureConfig();
	}

	void EnsureConfig()
	{
		if (config_loaded)
			return;

		config_loaded = true;
		config = new Config();

		string path = Path.Combine(Application.streamingAssetsPath, ConfigFileName);
		if (!File.Exists(path))
		{
			Debug.LogWarning("[nephila] config not found: " + path
				+ " — Nephila features disabled. Copy nephila_config.example.json and fill it in.");
			return;
		}

		try
		{
			string json = File.ReadAllText(path);
			JsonUtility.FromJsonOverwrite(json, config);
			if (DebugLog)
				Debug.Log("[nephila] config loaded | move=" + config.breakupMoveId
					+ " | collection=" + config.collectionId);
		}
		catch (Exception e)
		{
			Debug.LogError("[nephila] failed to read config: " + e.Message);
			config = new Config();
		}
	}

	// Runs the breakup-letter move with the given redacted words (comma-separated
	// upstream). on_success receives the generated letter text; on_error a message.
	public Coroutine RunBreakupLetter(string redactedWords, Action<string> on_success, Action<string> on_error)
	{
		EnsureConfig();

		if (!IsConfigured)
		{
			on_error?.Invoke("Nephila not configured");
			return null;
		}

		if (string.IsNullOrEmpty(config.openRouterKey))
		{
			on_error?.Invoke("OpenRouter key missing in config");
			return null;
		}

		var variables = new Dictionary<string, string>
		{
			{ "OPENROUTER_API_KEY", config.openRouterKey },
			{ "REDACTED_WORDS", redactedWords ?? "" }
		};

		return StartCoroutine(RunMoveRoutine(config.breakupMoveId, variables, on_success, on_error));
	}

	IEnumerator RunMoveRoutine(string moveId, Dictionary<string, string> variables,
		Action<string> on_success, Action<string> on_error)
	{
		string url = BaseUrl + "/api/public/moves/" + moveId + "/run";
		string body = BuildRunBody(config.collectionId, variables);

		if (DebugLog)
			Debug.Log("[nephila] run move " + moveId + " | vars=" + variables.Count);

		using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
		{
			byte[] payload = Encoding.UTF8.GetBytes(body);
			req.uploadHandler = new UploadHandlerRaw(payload);
			req.downloadHandler = new DownloadHandlerBuffer();
			req.SetRequestHeader("Content-Type", "application/json");
			req.SetRequestHeader("X-API-Key", config.apiKey);
			req.timeout = Mathf.Max(1, RequestTimeoutSeconds);

			yield return req.SendWebRequest();

			if (req.result != UnityWebRequest.Result.Success)
			{
				string msg = "http " + req.responseCode + " " + req.error;
				if (DebugLog)
					Debug.LogWarning("[nephila] run failed | " + msg + " | " + req.downloadHandler.text);
				on_error?.Invoke(msg);
				yield break;
			}

			string text = ExtractResultText(req.downloadHandler.text);
			if (string.IsNullOrEmpty(text))
			{
				on_error?.Invoke("empty result");
				yield break;
			}

			if (text.StartsWith("ERROR:"))
			{
				if (DebugLog)
					Debug.LogWarning("[nephila] move returned error | " + text);
				on_error?.Invoke(text);
				yield break;
			}

			if (DebugLog)
				Debug.Log("[nephila] letter received | " + text.Length + " chars");
			on_success?.Invoke(text);
		}
	}

	// Builds: {"collectionId":"...","contextConfig":{"dynamicVariableValues":{...}}}
	static string BuildRunBody(string collectionId, Dictionary<string, string> variables)
	{
		var sb = new StringBuilder(256);
		sb.Append("{\"collectionId\":");
		AppendJsonString(sb, collectionId);
		sb.Append(",\"contextConfig\":{\"dynamicVariableValues\":{");

		bool first = true;
		foreach (var kv in variables)
		{
			if (!first)
				sb.Append(',');
			first = false;
			AppendJsonString(sb, kv.Key);
			sb.Append(':');
			AppendJsonString(sb, kv.Value);
		}

		sb.Append("}}}");
		return sb.ToString();
	}

	static void AppendJsonString(StringBuilder sb, string s)
	{
		sb.Append('"');
		if (s != null)
		{
			foreach (char c in s)
			{
				switch (c)
				{
					case '"': sb.Append("\\\""); break;
					case '\\': sb.Append("\\\\"); break;
					case '\n': sb.Append("\\n"); break;
					case '\r': sb.Append("\\r"); break;
					case '\t': sb.Append("\\t"); break;
					default:
						if (c < 0x20)
							sb.Append("\\u").Append(((int)c).ToString("x4"));
						else
							sb.Append(c);
						break;
				}
			}
		}
		sb.Append('"');
	}

	// The run response is {"status":"success","data":{"results":[{"type":"text",
	// "data":"<letter>"}],...}}. JsonUtility can't read the nested dynamic shape
	// cleanly, so we pull the first results[].data string out directly.
	static string ExtractResultText(string json)
	{
		if (string.IsNullOrEmpty(json))
			return "";

		int resultsIdx = json.IndexOf("\"results\"", StringComparison.Ordinal);
		if (resultsIdx < 0)
			return "";

		// Find the first "data" key after the results array starts.
		int dataIdx = json.IndexOf("\"data\"", resultsIdx, StringComparison.Ordinal);
		if (dataIdx < 0)
			return "";

		int colon = json.IndexOf(':', dataIdx);
		if (colon < 0)
			return "";

		int i = colon + 1;
		while (i < json.Length && char.IsWhiteSpace(json[i]))
			i++;

		if (i >= json.Length || json[i] != '"')
			return "";

		i++; // past opening quote
		var sb = new StringBuilder(512);
		while (i < json.Length)
		{
			char c = json[i];
			if (c == '\\' && i + 1 < json.Length)
			{
				char n = json[i + 1];
				switch (n)
				{
					case '"': sb.Append('"'); break;
					case '\\': sb.Append('\\'); break;
					case '/': sb.Append('/'); break;
					case 'n': sb.Append('\n'); break;
					case 'r': sb.Append('\r'); break;
					case 't': sb.Append('\t'); break;
					case 'b': sb.Append('\b'); break;
					case 'f': sb.Append('\f'); break;
					case 'u':
						if (i + 5 < json.Length)
						{
							string hex = json.Substring(i + 2, 4);
							if (ushort.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
								System.Globalization.CultureInfo.InvariantCulture, out ushort code))
							{
								sb.Append((char)code);
								i += 4;
							}
						}
						break;
					default: sb.Append(n); break;
				}
				i += 2;
				continue;
			}

			if (c == '"')
				break;

			sb.Append(c);
			i++;
		}

		return sb.ToString();
	}
}
