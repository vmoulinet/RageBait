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

	[Header("Counts")]
	public int StartingMirrorCount = 6;

	[Header("Spawn")]
	public float SpawnInterval = 0.4f;

	[Header("Respawn")]
	public float RespawnDelay = 1.0f;
	public float RespawnRingSpacing = 0.75f;

	[Header("Debris Impact")]
	public float DebrisForce = 9f;
	public float DebrisRadius = 3.5f;
	public float DebrisUpwardModifier = 0.45f;
	public float DebrisImpactSpeedMultiplier = 0.35f;
	public float DebrisMaxBonusForce = 18f;
	public float DebrisDirectionalBackshift = 1.2f;
	public float DebrisDirectionalRadiusBonus = 0.75f;

	readonly List<MirrorSpawnPoint> spawnPoints = new List<MirrorSpawnPoint>();

	Vector3 cachedCenter;
	int cachedCenterFrame = -1;

	[HideInInspector] public readonly List<MirrorActor> ActiveMirrors = new List<MirrorActor>();

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

	[Header("Debug")]
	public bool EnableDebugBreakAll = true;
	public KeyCode BreakAllKey = KeyCode.R;

	void Update()
	{
		if (EnableDebugBreakAll && Input.GetKeyDown(BreakAllKey))
			BreakAllMirrors();
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

	public void OnMirrorBroken(MirrorActor mirror, Vector3 impactPoint)
	{
		if (mirror == null)
			return;

		if (mirror.CurrentSpawnPoint != null && mirror.CurrentSpawnPoint.CurrentMirror == mirror)
			mirror.CurrentSpawnPoint.CurrentMirror = null;

		if (SoundManager != null)
			SoundManager.PlayMirrorBreak(impactPoint);

		SpawnDebris(mirror, impactPoint);
		StartCoroutine(RespawnMirrorRoutine(mirror));

		if (WordManager != null)
			WordManager.OnMirrorBroken(mirror);

		if (ChoreographyManager != null)
			ChoreographyManager.RefreshTargets();
	}

	void SpawnDebris(MirrorActor mirror, Vector3 impactPoint)
	{
		if (DebrisPrefab == null || mirror == null)
			return;

		MirrorDebris debris = Instantiate(DebrisPrefab, DebrisRoot);
		debris.InitializeFromMirror(mirror);

		Vector3 impact_direction = mirror.LastBreakImpactDirection;
		float impact_speed = mirror.LastBreakImpactSpeed;

		float bonus_force = Mathf.Min(impact_speed * DebrisImpactSpeedMultiplier, DebrisMaxBonusForce);
		float applied_force = DebrisForce + bonus_force;
		float applied_radius = DebrisRadius;
		Vector3 applied_impact_point = impactPoint;

		if (impact_direction.sqrMagnitude > 0.0001f)
		{
			applied_impact_point -= impact_direction.normalized * DebrisDirectionalBackshift;
			applied_radius += DebrisDirectionalRadiusBonus;
		}

		debris.ApplyImpact(applied_impact_point, applied_force, applied_radius, DebrisUpwardModifier);

		if (mirror.DebugDraw)
		{
			Debug.Log(
				"[mirror_manager] debris | mirror=" + mirror.name +
				" | impact_point=" + impactPoint.ToString("F2") +
				" | applied_impact_point=" + applied_impact_point.ToString("F2") +
				" | impact_dir=" + impact_direction.ToString("F2") +
				" | impact_speed=" + impact_speed.ToString("F2") +
				" | applied_force=" + applied_force.ToString("F2") +
				" | applied_radius=" + applied_radius.ToString("F2")
			);
		}
	}

	IEnumerator RespawnMirrorRoutine(MirrorActor mirror)
	{
		yield return new WaitForSeconds(RespawnDelay);

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