using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MirrorManager : MonoBehaviour
{
	[Header("References")]
	public MirrorActor MirrorPrefab;
	public MirrorDebris DebrisPrefab;
	public Transform SpawnPointsRoot;
	public Transform ActiveMirrorsRoot;
	public Transform DebrisRoot;
	public ChoreographyManager ChoreographyManager;
	public WordManager WordManager;
	public SoundManager SoundManager;
	public DaddyLetterProjector DaddyLetterProjector;
	public MirrorMeltdown MirrorMeltdown;

	[Header("Counts")]
	public int StartingMirrorCount = 6;

	[Header("Spawn")]
	public float SpawnInterval = 0.4f;

	[Header("Respawn")]
	public float RespawnDelay = 1.0f;
	public float RespawnRingSpacing = 0.75f;
	[Tooltip("Decalage (s) entre deux respawns d'une meme rafale (ex: tous les miroirs casses avec R). " +
		"Evite de respawner 9 miroirs dans la meme frame (pic de cout : reactivation des lumieres HDRP, " +
		"RefreshTargets). A 0, comportement d'origine (tous en meme temps).")]
	public float RespawnStagger = 0.06f;

	[Header("Debris Limit")]
	public int MaxDebrisCount = 25;


	readonly List<MirrorSpawnPoint> spawnPoints = new List<MirrorSpawnPoint>();
	readonly List<MirrorDebris> debris_pool = new List<MirrorDebris>();

	Vector3 cachedCenter;
	int cachedCenterFrame = -1;

	// Echelonnement des respawns : si plusieurs miroirs cassent dans la meme rafale (ex: R),
	// on espace leurs respawns pour ne pas tout reactiver dans une seule frame.
	int respawn_burst_count = 0;
	int respawn_burst_frame = -1;

	[HideInInspector] public readonly List<MirrorActor> ActiveMirrors = new List<MirrorActor>();

	// Quand true, les respawns en attente sont geles (cf. MirrorMeltdown).
	// Des qu'il repasse a false, les miroirs en attente respawnent.
	[HideInInspector] public bool SuspendRespawn = false;

	public void Initialize(SimulationManager sim)
	{
		if (ChoreographyManager == null)
			ChoreographyManager = sim.ChoreographyManager;

		if (WordManager == null)
			WordManager = sim.WordManager;

		if (SoundManager == null)
			SoundManager = sim.SoundManager;

		Debug.Log(
			"[mirror_manager] initialize | choreography=" + (ChoreographyManager != null ? ChoreographyManager.name : "null") +
			" | word_manager=" + (WordManager != null ? WordManager.name : "null") +
			" | sound_manager=" + (SoundManager != null ? SoundManager.name : "null")
		);

		CacheSpawnPoints();
	}

	[Header("Break Boost")]
	public float BreakBoostMultiplier = 2f;
	public float BreakBoostDuration = 2f;

	[Header("Debug")]
	public bool EnableDebugBreakAll = true;
	public KeyCode BreakAllKey = KeyCode.R;

	void Update()
	{
		if (EnableDebugBreakAll && Input.GetKeyDown(BreakAllKey))
			BreakAllMirrors();
	}

	void ApplyBreakBoostToAll(MirrorActor brokenMirror)
	{
		for (int i = 0; i < ActiveMirrors.Count; i++)
		{
			MirrorActor mirror = ActiveMirrors[i];
			if (mirror == null || mirror == brokenMirror || mirror.IsBroken)
				continue;

			mirror.ApplySpeedBoost(BreakBoostMultiplier, BreakBoostDuration);
		}
	}

	public void BreakAllMirrors()
	{
		for (int i = ActiveMirrors.Count - 1; i >= 0; i--)
		{
			MirrorActor mirror = ActiveMirrors[i];
			if (mirror != null && !mirror.IsBroken)
				mirror.ForceBreak();
		}
	}

	// Remet le compteur du meltdown a zero (le jeu se passe activement). Appele
	// notamment sur stomp et WorldValidation, en plus du crash par pendule.
	public void ResetMeltdownTimer()
	{
		if (MirrorMeltdown != null)
			MirrorMeltdown.ResetTimer();
	}

	public void BootstrapMirrors()
	{
		if (MirrorPrefab == null)
		{
			Debug.LogError("MirrorManager: MirrorPrefab is missing.");
			return;
		}

		if (spawnPoints.Count == 0)
		{
			Debug.LogError("MirrorManager: no spawn points found.");
			return;
		}

		StartCoroutine(SpawnMirrorsRoutine());
	}

	IEnumerator SpawnMirrorsRoutine()
	{
		int count = Mathf.Max(0, StartingMirrorCount);

		for (int i = 0; i < count; i++)
		{
			MirrorSpawnPoint spawnPoint = spawnPoints[i % spawnPoints.Count];
			SpawnMirrorAt(spawnPoint, i);

			if (ChoreographyManager != null)
				ChoreographyManager.RefreshTargets();

			yield return new WaitForSeconds(SpawnInterval);
		}

		Debug.Log(
			"[mirror_manager] initial spawn finished | active=" + ActiveMirrors.Count +
			" | word_manager=" + (WordManager != null ? WordManager.name : "null")
		);
	}

	void CacheSpawnPoints()
	{
		spawnPoints.Clear();

		if (SpawnPointsRoot == null)
		{
			Debug.LogError("MirrorManager: SpawnPointsRoot is missing.");
			return;
		}

		MirrorSpawnPoint[] found = SpawnPointsRoot.GetComponentsInChildren<MirrorSpawnPoint>();
		for (int i = 0; i < found.Length; i++)
			spawnPoints.Add(found[i]);
	}

	void SpawnMirrorAt(MirrorSpawnPoint spawnPoint, int spawnIndex)
	{
		MirrorActor mirror = Instantiate(MirrorPrefab, ActiveMirrorsRoot);
		mirror.Initialize(this);
		mirror.ResetToSpawn(spawnPoint);

		Vector3 offset = GetSpawnOffset(spawnIndex);
		mirror.ApplySpawnOffset(offset);

		ActiveMirrors.Add(mirror);

		if (WordManager != null)
			WordManager.RegisterMirror(mirror);
	}

	Vector3 GetSpawnOffset(int spawnIndex)
	{
		if (spawnPoints.Count <= 0)
			return Vector3.zero;

		int ringIndex = spawnIndex / spawnPoints.Count;
		if (ringIndex <= 0)
			return Vector3.zero;

		int localIndex = spawnIndex % spawnPoints.Count;
		float angle = localIndex * Mathf.PI * 2f / Mathf.Max(1, spawnPoints.Count);
		float radius = RespawnRingSpacing * ringIndex;

		return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
	}

	public void OnMirrorBroken(MirrorActor mirror, Vector3 impactPoint, bool brokenByPendulum = false)
	{
		if (mirror == null)
			return;

		// Un crash par le pendule signifie que le jeu se passe activement :
		// on remet a zero le timer de meltdown (cf. MirrorMeltdown).
		if (brokenByPendulum && MirrorMeltdown != null)
			MirrorMeltdown.ResetTimer();

		if (mirror.CurrentSpawnPoint != null && mirror.CurrentSpawnPoint.CurrentMirror == mirror)
			mirror.CurrentSpawnPoint.CurrentMirror = null;

		if (SoundManager != null)
			SoundManager.PlayMirrorBreak(impactPoint);

		SpawnDebris(mirror, impactPoint);
		ApplyBreakBoostToAll(mirror);
		StartCoroutine(RespawnMirrorRoutine(mirror, NextRespawnStagger()));

		if (DaddyLetterProjector != null)
			DaddyLetterProjector.NotifyMirrorBroken();

		if (ChoreographyManager != null)
			ChoreographyManager.RefreshTargets();
	}

	void SpawnDebris(MirrorActor mirror, Vector3 impactPoint)
	{
		if (DebrisPrefab == null || mirror == null)
			return;

		SinkOldestDebrisIfNeeded();

		MirrorDebris debris = GetDebrisFromPool();
		debris.InitializeFromMirror(mirror);
		debris.ApplyImpact();
	}

	MirrorDebris GetDebrisFromPool()
	{
		for (int i = 0; i < debris_pool.Count; i++)
		{
			if (debris_pool[i] != null && !debris_pool[i].gameObject.activeSelf)
			{
				debris_pool[i].ResetForReuse();
				return debris_pool[i];
			}
		}

		MirrorDebris debris = Instantiate(DebrisPrefab, DebrisRoot);
		debris_pool.Add(debris);
		return debris;
	}

	void SinkOldestDebrisIfNeeded()
	{
		int active_count = 0;
		MirrorDebris oldest_active = null;
		float oldest_time = float.MaxValue;

		for (int i = 0; i < debris_pool.Count; i++)
		{
			MirrorDebris d = debris_pool[i];
			if (d != null && d.gameObject.activeSelf && !d.IsSinking)
			{
				active_count++;
				if (d.ActivateTime < oldest_time)
				{
					oldest_time = d.ActivateTime;
					oldest_active = d;
				}
			}
		}

		if (active_count < MaxDebrisCount || oldest_active == null)
			return;

		oldest_active.StartSinking();
	}

	// Renvoie le decalage a appliquer au prochain respawn pour etaler une rafale.
	// Les miroirs casses dans la meme frame (ou des frames consecutives) recoivent un decalage
	// croissant ; des qu'une frame passe sans nouveau bris, le compteur repart de zero.
	float NextRespawnStagger()
	{
		if (Time.frameCount - respawn_burst_frame > 1)
			respawn_burst_count = 0;

		float stagger = respawn_burst_count * Mathf.Max(0f, RespawnStagger);
		respawn_burst_count++;
		respawn_burst_frame = Time.frameCount;
		return stagger;
	}

	IEnumerator RespawnMirrorRoutine(MirrorActor mirror, float extraDelay)
	{
		yield return new WaitForSeconds(RespawnDelay + Mathf.Max(0f, extraDelay));

		// Tant que le respawn est suspendu (meltdown en cours), on attend.
		while (SuspendRespawn)
			yield return null;

		if (mirror == null)
			yield break;

		if (spawnPoints.Count == 0)
			yield break;

		int respawnIndex = GetActiveMirrorIndex(mirror);
		if (respawnIndex < 0)
			respawnIndex = 0;

		MirrorSpawnPoint spawnPoint = spawnPoints[respawnIndex % spawnPoints.Count];
		mirror.ResetToSpawn(spawnPoint);

		if (ChoreographyManager != null)
			ChoreographyManager.RefreshTargets();
	}

	int GetActiveMirrorIndex(MirrorActor mirror)
	{
		for (int i = 0; i < ActiveMirrors.Count; i++)
		{
			if (ActiveMirrors[i] == mirror)
				return i;
		}
		return -1;
	}

	public Vector3 GetSimulationCenter()
	{
		if (Time.frameCount == cachedCenterFrame)
			return cachedCenter;

		cachedCenterFrame = Time.frameCount;

		if (ActiveMirrors.Count == 0)
		{
			cachedCenter = transform.position;
			return cachedCenter;
		}

		Vector3 center = Vector3.zero;
		int count = 0;

		for (int i = 0; i < ActiveMirrors.Count; i++)
		{
			MirrorActor mirror = ActiveMirrors[i];
			if (mirror == null || mirror.IsBroken || !mirror.gameObject.activeInHierarchy)
				continue;

			center += mirror.WorldPosition;
			count++;
		}

		cachedCenter = count == 0 ? transform.position : center / count;
		return cachedCenter;
	}
}