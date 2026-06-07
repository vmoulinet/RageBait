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
		public string breakupMoveId = "";   // the (now generic) move id
		public string openRouterKey = "";   // sk-or-... passed to the move at run time
		public string systemPrompt = "";    // the LLM system prompt, sent to the move

		// Name of an EXTERNAL folder (filled by someone else) holding word lists.
		// When non-empty and a file exists there, the newest file's words REPLACE
		// the redacted words for the generation (stronger influence). Empty = off.
		public string externalWordsFolderName = "";
	}

	[Header("Config")]
	[Tooltip("File under StreamingAssets, kept out of git. Holds the API keys and ids.")]
	public string ConfigFileName = "nephila_config.json";

	[Header("Endpoint")]
	public string BaseUrl = "https://nephila.app";
	[Tooltip("Hard timeout for a move run (LLM calls can take several seconds).")]
	public int RequestTimeoutSeconds = 60;

	[Header("Output")]
	[Tooltip("If true, each generated letter is also stored back into the Nephila " +
		"collection (the Digest auto-files it, generates a description and embedding).")]
	public bool StoreInCollection = true;
	[Tooltip("If true, store the letter in a folder named after the current date " +
		"(yyyy-MM-dd), creating that folder if it doesn't exist yet. If false, the " +
		"Digest auto-organizes the output.")]
	public bool UseDailyFolder = true;
	[Tooltip("If true, also upload the raw redacted words (the seed) as a separate " +
		"file into a dedicated folder, alongside each generated letter.")]
	public bool StoreRedactedWords = true;
	[Tooltip("Folder name where the redacted-words seed files are stored.")]
	public string RedactedFolderName = "redacted";
	[Tooltip("If true, upload an empty marker .txt file per mirror smashed.")]
	public bool StoreSmashedMarkers = true;
	[Tooltip("Folder name where the per-mirror empty marker files are stored.")]
	public string SmashedFolderName = "SMASHED";

	[Header("Debug")]
	public bool DebugLog = false;

	Config config;
	bool config_loaded = false;
	string smashed_folder_id = null; // cached once resolved (mirrors break in bursts)

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
				+ " ; Nephila features disabled. Copy nephila_config.example.json and fill it in.");
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

		return StartCoroutine(RunBreakupLetterRoutine(redactedWords ?? "", on_success, on_error));
	}

	IEnumerator RunBreakupLetterRoutine(string redactedWords, Action<string> on_success, Action<string> on_error)
	{
		// If an external word-list folder is configured and holds a file, the newest
		// file's words REPLACE the redacted words (stronger influence).
		string words = redactedWords;
		if (!string.IsNullOrEmpty(config.externalWordsFolderName))
		{
			string external = null;
			yield return StartCoroutine(FetchLatestExternalWords(s => external = s));
			if (!string.IsNullOrEmpty(external))
			{
				words = external.Trim();
				if (DebugLog)
					Debug.Log("[nephila] using EXTERNAL words (override redacted) | " + words);
			}
		}

		var variables = new Dictionary<string, string>
		{
			{ "OPENROUTER_API_KEY", config.openRouterKey },
			{ "REDACTED_WORDS", words },
			{ "SYSTEM_PROMPT", config.systemPrompt ?? "" }
		};

		yield return StartCoroutine(RunMoveRoutine(config.breakupMoveId, variables, words, on_success, on_error));
	}

	// Fetches the words from the newest file in the external word-list folder.
	// Calls on_words with the file's text, or null if none / on failure.
	IEnumerator FetchLatestExternalWords(Action<string> on_words)
	{
		// 1) Get the collection JSON.
		string colJson = null;
		yield return StartCoroutine(GetCollectionJson(s => colJson = s));
		if (string.IsNullOrEmpty(colJson))
		{
			on_words(null);
			yield break;
		}

		// 2) Find the newest item id inside the external folder.
		string itemId = FindNewestItemIdInFolder(colJson, config.externalWordsFolderName);
		if (string.IsNullOrEmpty(itemId))
		{
			if (DebugLog)
				Debug.Log("[nephila] external folder '" + config.externalWordsFolderName + "' empty or absent");
			on_words(null);
			yield break;
		}

		// 3) Resolve its signed URL.
		string fileUrl = null;
		yield return StartCoroutine(GetItemUrl(itemId, u => fileUrl = u));
		if (string.IsNullOrEmpty(fileUrl))
		{
			on_words(null);
			yield break;
		}

		// 4) Download the text.
		using (UnityWebRequest req = UnityWebRequest.Get(fileUrl))
		{
			req.timeout = Mathf.Max(1, RequestTimeoutSeconds);
			yield return req.SendWebRequest();
			if (req.result != UnityWebRequest.Result.Success)
			{
				if (DebugLog)
					Debug.LogWarning("[nephila] external words download failed | " + req.responseCode + " " + req.error);
				on_words(null);
				yield break;
			}
			on_words(req.downloadHandler.text);
		}
	}

	IEnumerator GetCollectionJson(Action<string> on_json)
	{
		string url = BaseUrl + "/api/public/collections/" + config.collectionId;
		using (UnityWebRequest req = UnityWebRequest.Get(url))
		{
			req.SetRequestHeader("X-API-Key", config.apiKey);
			req.timeout = Mathf.Max(1, RequestTimeoutSeconds);
			yield return req.SendWebRequest();
			on_json(req.result == UnityWebRequest.Result.Success ? req.downloadHandler.text : null);
		}
	}

	IEnumerator GetItemUrl(string itemId, Action<string> on_url)
	{
		string url = BaseUrl + "/api/public/items/" + itemId + "/url";
		using (UnityWebRequest req = UnityWebRequest.Get(url))
		{
			req.SetRequestHeader("X-API-Key", config.apiKey);
			req.timeout = Mathf.Max(1, RequestTimeoutSeconds);
			yield return req.SendWebRequest();
			if (req.result != UnityWebRequest.Result.Success)
			{
				on_url(null);
				yield break;
			}
			on_url(ExtractJsonValue(req.downloadHandler.text, "url"));
		}
	}

	// Uploads one empty marker file into the SMASHED folder. Call once per mirror
	// smashed. Best-effort and silent; needs only the Nephila key + collection
	// (no move / OpenRouter). No-op if disabled or not configured.
	public void UploadSmashedMarker()
	{
		EnsureConfig();

		if (!StoreSmashedMarkers)
			return;

		if (config == null || string.IsNullOrEmpty(config.apiKey) || string.IsNullOrEmpty(config.collectionId))
		{
			if (DebugLog)
				Debug.LogWarning("[nephila] smashed marker skipped ; missing apiKey/collectionId");
			return;
		}

		StartCoroutine(UploadSmashedMarkerRoutine());
	}

	IEnumerator UploadSmashedMarkerRoutine()
	{
		// Mirrors break in bursts; cache the folder id so we don't GET the whole
		// collection for every single marker.
		if (string.IsNullOrEmpty(smashed_folder_id))
		{
			string folderId = null;
			yield return StartCoroutine(ResolveFolder(SmashedFolderName, id => folderId = id));
			smashed_folder_id = folderId;
		}

		if (string.IsNullOrEmpty(smashed_folder_id))
		{
			if (DebugLog)
				Debug.LogWarning("[nephila] SMASHED folder unavailable, skipping marker");
			yield break;
		}

		string fileName = DateTime.Now.ToString("yy.MM.dd_HH-mm-ss") + "_smashed.txt";
		yield return StartCoroutine(UploadTextFile(smashed_folder_id, fileName, ""));
	}

	IEnumerator RunMoveRoutine(string moveId, Dictionary<string, string> variables,
		string redactedWords, Action<string> on_success, Action<string> on_error)
	{
		// Decide where the output goes. When daily folders are on, resolve (or
		// create) a folder named after today's date and target it explicitly;
		// otherwise let the server auto-organize (addOutputToFolder = true).
		string targetFolderId = null;
		if (StoreInCollection && UseDailyFolder)
		{
			string folderName = GetTodayFolderName();
			yield return StartCoroutine(ResolveFolder(folderName, id => targetFolderId = id));
			// If resolution failed we fall back to auto-organize rather than losing the letter.
		}

		string url = BaseUrl + "/api/public/moves/" + moveId + "/run";
		string body = BuildRunBody(config.collectionId, variables, StoreInCollection, targetFolderId);

		if (DebugLog)
			Debug.Log("[nephila] run move " + moveId + " | vars=" + variables.Count
				+ (StoreInCollection ? (" | folder=" + (targetFolderId ?? "auto")) : ""));

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

			// Best-effort: archive the raw redacted words as a separate seed file.
			// Failure here must not stop the letter from being delivered.
			if (StoreRedactedWords && !string.IsNullOrEmpty(redactedWords))
				yield return StartCoroutine(StoreRedactedWordsFile(redactedWords));

			on_success?.Invoke(text);
		}
	}

	// Uploads the raw redacted words (CSV) as a text file into the redacted folder,
	// named "yy.MM.dd_HH-mm-ss_redacted.txt". Creates the folder if needed.
	IEnumerator StoreRedactedWordsFile(string redactedWords)
	{
		string folderId = null;
		yield return StartCoroutine(ResolveFolder(RedactedFolderName, id => folderId = id));
		if (string.IsNullOrEmpty(folderId))
		{
			if (DebugLog)
				Debug.LogWarning("[nephila] redacted folder unavailable, skipping seed upload");
			yield break;
		}

		string fileName = DateTime.Now.ToString("yy.MM.dd_HH-mm-ss") + "_redacted.txt";
		yield return StartCoroutine(UploadTextFile(folderId, fileName, redactedWords));
	}

	IEnumerator UploadTextFile(string folderId, string fileName, string content)
	{
		string url = BaseUrl + "/api/public/items/upload";
		string boundary = "----NephilaBoundary" + Guid.NewGuid().ToString("N");

		// UnityWebRequest's multipart file section rejects empty body data, so an
		// "empty" marker file gets a single newline (smallest possible payload).
		byte[] fileBytes = Encoding.UTF8.GetBytes(string.IsNullOrEmpty(content) ? "\n" : content);

		var parts = new List<IMultipartFormSection>
		{
			new MultipartFormFileSection("file", fileBytes, fileName, "text/plain"),
			new MultipartFormDataSection("collectionId", config.collectionId),
			new MultipartFormDataSection("folderId", folderId)
		};
		byte[] body = UnityWebRequest.SerializeFormSections(parts, Encoding.UTF8.GetBytes(boundary));

		using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
		{
			req.uploadHandler = new UploadHandlerRaw(body);
			req.downloadHandler = new DownloadHandlerBuffer();
			req.SetRequestHeader("Content-Type", "multipart/form-data; boundary=" + boundary);
			req.SetRequestHeader("X-API-Key", config.apiKey);
			req.timeout = Mathf.Max(1, RequestTimeoutSeconds);
			yield return req.SendWebRequest();

			if (req.result != UnityWebRequest.Result.Success)
			{
				if (DebugLog)
					Debug.LogWarning("[nephila] seed upload failed | " + req.responseCode + " "
						+ req.error + " | " + req.downloadHandler.text);
				yield break;
			}

			if (DebugLog)
				Debug.Log("[nephila] redacted seed stored | " + fileName);
		}
	}

	// Builds: {"collectionId":"...","contextConfig":{"dynamicVariableValues":{...}}
	//          [,"addOutputToFolder": <folderId-string> | true]}
	// When storeOutput is true, the server files the result back into the
	// collection. A folderId targets a specific folder; otherwise true lets the
	// Digest auto-organize it (with description + embedding).
	static string BuildRunBody(string collectionId, Dictionary<string, string> variables,
		bool storeOutput, string folderId)
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

		sb.Append("}}");
		if (storeOutput)
		{
			sb.Append(",\"addOutputToFolder\":");
			if (!string.IsNullOrEmpty(folderId))
				AppendJsonString(sb, folderId);
			else
				sb.Append("true");
		}
		sb.Append('}');
		return sb.ToString();
	}

	// Today's folder name, e.g. "2026-06-07".
	static string GetTodayFolderName()
	{
		return DateTime.Now.ToString("yyyy-MM-dd");
	}

	// Finds the collection folder named folderName; creates it if missing.
	// Calls on_resolved with the folder id, or null on failure.
	IEnumerator ResolveFolder(string folderName, Action<string> on_resolved)
	{
		// 1) Look for an existing folder with this name.
		string existing = null;
		yield return StartCoroutine(FindFolderId(folderName, id => existing = id));
		if (!string.IsNullOrEmpty(existing))
		{
			if (DebugLog)
				Debug.Log("[nephila] folder exists | " + folderName + " -> " + existing);
			on_resolved(existing);
			yield break;
		}

		// 2) Create it.
		string created = null;
		yield return StartCoroutine(CreateFolder(folderName, id => created = id));
		if (DebugLog)
			Debug.Log("[nephila] folder " + (created != null ? "created" : "create FAILED")
				+ " | " + folderName + (created != null ? (" -> " + created) : ""));
		on_resolved(created);
	}

	IEnumerator FindFolderId(string folderName, Action<string> on_found)
	{
		string url = BaseUrl + "/api/public/collections/" + config.collectionId;
		using (UnityWebRequest req = UnityWebRequest.Get(url))
		{
			req.SetRequestHeader("X-API-Key", config.apiKey);
			req.timeout = Mathf.Max(1, RequestTimeoutSeconds);
			yield return req.SendWebRequest();

			if (req.result != UnityWebRequest.Result.Success)
			{
				if (DebugLog)
					Debug.LogWarning("[nephila] list folders failed | " + req.responseCode + " " + req.error);
				on_found(null);
				yield break;
			}

			on_found(FindFolderIdInJson(req.downloadHandler.text, folderName));
		}
	}

	IEnumerator CreateFolder(string folderName, Action<string> on_created)
	{
		string url = BaseUrl + "/api/public/folders/create";

		var sb = new StringBuilder(128);
		sb.Append("{\"collectionId\":");
		AppendJsonString(sb, config.collectionId);
		sb.Append(",\"type\":");
		AppendJsonString(sb, folderName);
		sb.Append('}');

		using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
		{
			req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(sb.ToString()));
			req.downloadHandler = new DownloadHandlerBuffer();
			req.SetRequestHeader("Content-Type", "application/json");
			req.SetRequestHeader("X-API-Key", config.apiKey);
			req.timeout = Mathf.Max(1, RequestTimeoutSeconds);
			yield return req.SendWebRequest();

			if (req.result != UnityWebRequest.Result.Success)
			{
				if (DebugLog)
					Debug.LogWarning("[nephila] create folder failed | " + req.responseCode + " "
						+ req.error + " | " + req.downloadHandler.text);
				on_created(null);
				yield break;
			}

			// Response: {"success":true,"folder":{"id":"...","type":"...",...}}
			on_created(ExtractJsonValue(req.downloadHandler.text, "id"));
		}
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

	// In the collection JSON, "folders" is an object keyed by folder name:
	// {"folders":{"2026-06-07":{"id":"...","type":"2026-06-07",...}}}.
	// Locates the entry for folderName and returns its "id".
	static string FindFolderIdInJson(string json, string folderName)
	{
		if (string.IsNullOrEmpty(json))
			return null;

		// Scope the search to the folders object so we don't match an id elsewhere.
		int foldersIdx = json.IndexOf("\"folders\"", StringComparison.Ordinal);
		int searchFrom = foldersIdx >= 0 ? foldersIdx : 0;

		string key = "\"" + folderName + "\"";
		int keyIdx = json.IndexOf(key, searchFrom, StringComparison.Ordinal);
		if (keyIdx < 0)
			return null;

		return ExtractJsonValue(json, "id", keyIdx);
	}

	// Within the named folder's block, finds the item ("_id") whose "createdAt" is
	// the most recent (ISO timestamps compare lexically). Returns null if none.
	static string FindNewestItemIdInFolder(string json, string folderName)
	{
		if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(folderName))
			return null;

		int foldersIdx = json.IndexOf("\"folders\"", StringComparison.Ordinal);
		int searchFrom = foldersIdx >= 0 ? foldersIdx : 0;
		int keyIdx = json.IndexOf("\"" + folderName + "\"", searchFrom, StringComparison.Ordinal);
		if (keyIdx < 0)
			return null;

		// Isolate this folder's object by brace matching from the first '{' after the key.
		int open = json.IndexOf('{', keyIdx);
		if (open < 0)
			return null;

		int depth = 0;
		int end = -1;
		for (int i = open; i < json.Length; i++)
		{
			char c = json[i];
			if (c == '{') depth++;
			else if (c == '}') { depth--; if (depth == 0) { end = i; break; } }
		}
		if (end < 0)
			return null;

		string block = json.Substring(open, end - open + 1);

		// Scan each "_id" and the "createdAt" that follows it; keep the newest.
		string bestId = null;
		string bestDate = null;
		int pos = 0;
		while (true)
		{
			int idIdx = block.IndexOf("\"_id\"", pos, StringComparison.Ordinal);
			if (idIdx < 0)
				break;

			string id = ExtractJsonValue(block, "_id", idIdx);
			string date = null;
			int dateIdx = block.IndexOf("\"createdAt\"", idIdx, StringComparison.Ordinal);
			if (dateIdx >= 0)
				date = ExtractJsonValue(block, "createdAt", dateIdx);

			if (!string.IsNullOrEmpty(id))
			{
				string cmp = date ?? "";
				if (bestId == null || string.CompareOrdinal(cmp, bestDate ?? "") > 0)
				{
					bestId = id;
					bestDate = cmp;
				}
			}

			pos = idIdx + 5;
		}

		return bestId;
	}

	// Returns the string value of the first "<key>":"<value>" occurring at or
	// after startIndex, or null. Only handles string values (enough for ids).
	static string ExtractJsonValue(string json, string key, int startIndex = 0)
	{
		if (string.IsNullOrEmpty(json))
			return null;

		string token = "\"" + key + "\"";
		int k = json.IndexOf(token, startIndex, StringComparison.Ordinal);
		if (k < 0)
			return null;

		int colon = json.IndexOf(':', k + token.Length);
		if (colon < 0)
			return null;

		int i = colon + 1;
		while (i < json.Length && char.IsWhiteSpace(json[i]))
			i++;

		if (i >= json.Length || json[i] != '"')
			return null;

		i++; // past opening quote
		int start = i;
		var sb = new StringBuilder(48);
		while (i < json.Length)
		{
			char c = json[i];
			if (c == '\\' && i + 1 < json.Length)
			{
				sb.Append(json[i + 1]);
				i += 2;
				continue;
			}
			if (c == '"')
				break;
			sb.Append(c);
			i++;
		}

		return i > start ? sb.ToString() : null;
	}
}
