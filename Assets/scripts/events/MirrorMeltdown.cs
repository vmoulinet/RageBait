using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

// Regle : lorsque les miroirs ne sont pas "positionnes" (formation Triangle non
// stable) depuis MeltdownDelay secondes, le meltdown se declenche :
//  1. le respawn est gele,
//  2. tous les miroirs se tournent vers la camera (face reflechissante),
//  3. les Spotlight se dim out a 0 et l'exposure du HDRI Sky descend a 0 (fondu),
//  4. les "Individual PointLight" se chargent et les miroirs explosent un a un,
//  5. a la fin : lumieres + exposure restaurees, respawn reactive.
//
// Le timer est remis a zero a chaque crash de miroir par le pendule
// (cf. MirrorManager.OnMirrorBroken -> ResetTimer).
public class MirrorMeltdown : MonoBehaviour
{
	[Header("References")]
	public ChoreographyManager ChoreographyManager;
	public MirrorManager MirrorManager;
	[Tooltip("Cible vers laquelle les miroirs orientent leur face reflechissante. Fallback: Camera.main.")]
	public Transform CameraTarget;
	[Tooltip("GameObject parent des Spotlight a faire dim out (toutes ses Light enfants).")]
	public Transform SpotlightsRoot;
	[Tooltip("Global Volume contenant le HDRI Sky dont on pilote l'exposure.")]
	public Volume GlobalVolume;

	[Header("Trigger")]
	[Tooltip("Temps (s) en formation Triangle NON stable avant de declencher le meltdown.")]
	public float MeltdownDelay = 10f;

	[Header("Sequence")]
	[Tooltip("Delai (s) entre le demarrage de la charge de deux miroirs consecutifs.")]
	public float ChargeInterval = 1f;
	[Tooltip("Duree (s) de la charge lumineuse d'un miroir avant qu'il n'explose.")]
	public float ChargeDuration = 2f;
	[Tooltip("Duree (s) du fondu d'extinction (dim des Spotlight + exposure) au debut du meltdown.")]
	public float FadeOutDuration = 0.5f;
	[Tooltip("Duree (s) du fondu de rallumage (Spotlight + exposure) a la fin du meltdown.")]
	public float FadeInDuration = 0.5f;
	[Tooltip("Temps (s) laisse aux miroirs immobilises pour pivoter vers la camera avant le fondu.")]
	public float FaceDuration = 1f;
	[Tooltip("Temps (s) apres l'explosion du dernier miroir avant que les nouveaux miroirs ne respawnent (dans le noir).")]
	public float MeltdownRespawnDelay = 3f;
	[Tooltip("Temps (s) apres l'explosion du dernier miroir avant que les lumieres / l'exposure ne se rallument. Doit etre >= MeltdownRespawnDelay pour que le respawn precede le rallumage.")]
	public float RelightDelay = 7f;

	// Bascule du panneau X pendant l'attente face camera, depuis le repos (0) :
	// d'abord en arriere (PanelTiltBackward) puis en avant (PanelTiltForward).
	[Header("Tilt")]
	[Tooltip("Angle (deg) de la 1ere bascule : en arriere.")]
	public float PanelTiltBackward = -30f;
	[Tooltip("Angle (deg) de la 2eme bascule : en avant. C'est l'angle final tenu pendant les explosions.")]
	public float PanelTiltForward = 45f;
	[Tooltip("Duree (s) de chaque segment de la bascule du panneau.")]
	public float PanelTiltDuration = 0.5f;

	[Header("Light")]
	[Tooltip("Nom de la Light enfant de chaque miroir a faire monter (comme WorldValidation).")]
	public string IndividualLightName = "Individual PointLight";
	[Tooltip("Intensite atteinte par la Light au moment de l'explosion.")]
	public float ChargeMaxIntensity = 120f;

	[Header("Explosion")]
	[Tooltip("Poussee verticale supplementaire appliquee aux debris (explosion en l'air).")]
	public float ExplodeUpwardBoost = 8f;

	[Header("Debug")]
	public bool DebugLog = true;

	float not_positioned_timer = 0f;
	bool meltdown_running = false;

	// Etat sauvegarde / restaure pendant le meltdown.
	Light[] spotlights;
	float[] spotlight_start_intensity;
	HDRISky hdri_sky;
	float exposure_start = 0f;

	// Remet le compteur a zero. Appele notamment a chaque crash de miroir par le
	// pendule (cf. MirrorManager.OnMirrorBroken) : le jeu se passe activement.
	public void ResetTimer()
	{
		not_positioned_timer = 0f;
	}

	void Update()
	{
		if (meltdown_running)
			return;

		if (ChoreographyManager == null)
			return;

		bool positioned =
			ChoreographyManager.CurrentState == ChoreographyState.Triangle &&
			ChoreographyManager.IsTriangleCurrentlyStable;

		if (positioned)
		{
			not_positioned_timer = 0f;
			return;
		}

		not_positioned_timer += Time.deltaTime;

		if (not_positioned_timer >= MeltdownDelay)
			StartMeltdown();
	}

	void StartMeltdown()
	{
		if (meltdown_running)
			return;

		if (MirrorManager == null || MirrorManager.ActiveMirrors == null)
			return;

		List<MirrorActor> targets = new List<MirrorActor>();
		List<MirrorActor> active = MirrorManager.ActiveMirrors;
		for (int i = 0; i < active.Count; i++)
		{
			MirrorActor mirror = active[i];
			if (mirror != null && !mirror.IsBroken && mirror.gameObject.activeInHierarchy)
				targets.Add(mirror);
		}

		if (targets.Count == 0)
		{
			not_positioned_timer = 0f;
			return;
		}

		meltdown_running = true;
		not_positioned_timer = 0f;

		if (DebugLog)
			Debug.Log("[mirror_meltdown] start | mirrors=" + targets.Count);

		StartCoroutine(MeltdownRoutine(targets));
	}

	IEnumerator MeltdownRoutine(List<MirrorActor> targets)
	{
		// 1. Geler le respawn et l'auto-cycle de la choregraphie (sinon une
		//    transition de pattern ecraserait facing overrides et gel).
		if (MirrorManager != null)
			MirrorManager.SuspendRespawn = true;
		if (ChoreographyManager != null)
			ChoreographyManager.SuspendAutoCycle = true;

		// 2. Immobiliser les miroirs puis les tourner vers la camera (face
		//    reflechissante). On laisse FaceDuration pour qu'ils pivotent.
		FreezeMirrors(targets);
		FaceMirrorsToCamera(targets);
		if (FaceDuration > 0f)
			yield return new WaitForSeconds(FaceDuration);

		// 2b. Bascule du panneau X : en arriere (PanelTiltBackward) puis en avant
		//     (PanelTiltForward), pendant qu'ils attendent face camera.
		yield return StartCoroutine(TiltPanels(targets));

		// 3. Preparer puis fondre les Spotlight et l'exposure vers 0.
		CacheSpotlights();
		CacheExposure();
		yield return StartCoroutine(FadeOutLightsAndExposure());

		// 4. Charges + explosions une a une.
		int remaining = targets.Count;
		for (int i = 0; i < targets.Count; i++)
		{
			StartCoroutine(ChargeAndExplode(targets[i], () => remaining--));

			if (i < targets.Count - 1)
				yield return new WaitForSeconds(Mathf.Max(0f, ChargeInterval));
		}

		while (remaining > 0)
			yield return null;

		// Les deux delais courent depuis l'explosion du dernier miroir. On garantit
		// que le respawn precede le rallumage (clamp du relight a >= respawn).
		float respawn_delay = Mathf.Max(0f, MeltdownRespawnDelay);
		float relight_delay = Mathf.Max(respawn_delay, RelightDelay);

		// 5. Attendre, puis respawner les nouveaux miroirs (toujours dans le noir).
		if (respawn_delay > 0f)
			yield return new WaitForSeconds(respawn_delay);

		if (ChoreographyManager != null)
			ChoreographyManager.SuspendAutoCycle = false;
		if (MirrorManager != null)
			MirrorManager.SuspendRespawn = false;

		// 6. Attendre le reste du delai de rallumage, puis restaurer lumieres + exposure.
		float remaining_dark = relight_delay - respawn_delay;
		if (remaining_dark > 0f)
			yield return new WaitForSeconds(remaining_dark);

		yield return StartCoroutine(FadeInLightsAndExposure());

		meltdown_running = false;

		if (DebugLog)
			Debug.Log("[mirror_meltdown] done");
	}

	void FreezeMirrors(List<MirrorActor> targets)
	{
		for (int i = 0; i < targets.Count; i++)
		{
			if (targets[i] != null && !targets[i].IsBroken)
				targets[i].SetMovementFrozen(true);
		}
	}

	// Bascule synchrone du panneau X : 0 -> arriere -> avant, chaque segment sur
	// PanelTiltDuration secondes. L'angle avant est tenu pour la suite.
	IEnumerator TiltPanels(List<MirrorActor> targets)
	{
		for (int i = 0; i < targets.Count; i++)
		{
			if (targets[i] != null && !targets[i].IsBroken)
				targets[i].SetPanelXOverride(true);
		}

		yield return StartCoroutine(LerpPanels(targets, 0f, PanelTiltBackward, PanelTiltDuration));
		yield return StartCoroutine(LerpPanels(targets, PanelTiltBackward, PanelTiltForward, PanelTiltDuration));
	}

	IEnumerator LerpPanels(List<MirrorActor> targets, float from, float to, float duration)
	{
		duration = Mathf.Max(0f, duration);

		if (duration <= 0f)
		{
			ApplyPanelAngle(targets, to);
			yield break;
		}

		float elapsed = 0f;
		while (elapsed < duration)
		{
			float angle = Mathf.Lerp(from, to, elapsed / duration);
			ApplyPanelAngle(targets, angle);
			elapsed += Time.deltaTime;
			yield return null;
		}

		ApplyPanelAngle(targets, to);
	}

	void ApplyPanelAngle(List<MirrorActor> targets, float angle)
	{
		for (int i = 0; i < targets.Count; i++)
		{
			if (targets[i] != null && !targets[i].IsBroken)
				targets[i].SetPanelXValue(angle);
		}
	}

	void FaceMirrorsToCamera(List<MirrorActor> targets)
	{
		Transform target = CameraTarget != null ? CameraTarget : (Camera.main != null ? Camera.main.transform : null);
		if (target == null)
			return;

		for (int i = 0; i < targets.Count; i++)
		{
			MirrorActor mirror = targets[i];
			if (mirror == null || mirror.IsBroken)
				continue;

			Vector3 to_camera = target.position - mirror.WorldPosition;
			to_camera.y = 0f;
			if (to_camera.sqrMagnitude < 0.0001f)
				continue;

			// Face reflechissante vers la camera : meme convention que
			// FaceAllMirrorsToDaddy (oriente l'oppose du forward vers la cible).
			mirror.SetFacingOverride(-to_camera.normalized);
		}
	}

	// === Spotlights ==================================================================

	void CacheSpotlights()
	{
		spotlights = null;
		spotlight_start_intensity = null;

		if (SpotlightsRoot == null)
			return;

		spotlights = SpotlightsRoot.GetComponentsInChildren<Light>(true);
		spotlight_start_intensity = new float[spotlights.Length];
		for (int i = 0; i < spotlights.Length; i++)
			spotlight_start_intensity[i] = spotlights[i] != null ? spotlights[i].intensity : 0f;
	}

	// === Exposure (HDRI Sky du Global Volume) ========================================

	void CacheExposure()
	{
		hdri_sky = null;
		exposure_start = 0f;

		if (GlobalVolume == null || GlobalVolume.profile == null)
			return;

		if (GlobalVolume.profile.TryGet(out hdri_sky) && hdri_sky != null)
			exposure_start = hdri_sky.exposure.value;
	}

	IEnumerator FadeOutLightsAndExposure()
	{
		yield return Fade(1f, 0f, FadeOutDuration);
	}

	IEnumerator FadeInLightsAndExposure()
	{
		yield return Fade(0f, 1f, FadeInDuration);
	}

	// factor 1 = etat de depart (lumieres/exposure pleines), 0 = eteint.
	IEnumerator Fade(float from, float to, float fade_duration)
	{
		float duration = Mathf.Max(0f, fade_duration);

		if (duration <= 0f)
		{
			ApplyFactor(to);
			yield break;
		}

		float elapsed = 0f;
		while (elapsed < duration)
		{
			float factor = Mathf.Lerp(from, to, elapsed / duration);
			ApplyFactor(factor);
			elapsed += Time.deltaTime;
			yield return null;
		}

		ApplyFactor(to);
	}

	void ApplyFactor(float factor)
	{
		if (spotlights != null && spotlight_start_intensity != null)
		{
			for (int i = 0; i < spotlights.Length; i++)
			{
				if (spotlights[i] != null)
					spotlights[i].intensity = spotlight_start_intensity[i] * factor;
			}
		}

		if (hdri_sky != null)
		{
			hdri_sky.exposure.value = exposure_start * factor;
			hdri_sky.exposure.overrideState = true;
		}
	}

	// === Charge + explosion d'un miroir ==============================================

	IEnumerator ChargeAndExplode(MirrorActor mirror, System.Action on_complete)
	{
		Light light = FindIndividualLight(mirror);
		float start_intensity = light != null ? light.intensity : 0f;

		float elapsed = 0f;
		float duration = Mathf.Max(0.0001f, ChargeDuration);

		while (elapsed < duration)
		{
			// Le miroir peut casser autrement (pendule) pendant la charge.
			if (mirror == null || mirror.IsBroken)
			{
				on_complete?.Invoke();
				yield break;
			}

			if (light != null)
				light.intensity = Mathf.Lerp(start_intensity, ChargeMaxIntensity, elapsed / duration);

			elapsed += Time.deltaTime;
			yield return null;
		}

		if (light != null)
			light.intensity = ChargeMaxIntensity;

		if (mirror != null && !mirror.IsBroken)
		{
			if (DebugLog)
				Debug.Log("[mirror_meltdown] explode | " + mirror.name);

			// Restaurer l'intensite de la light avant le bris : le GameObject est
			// reutilise au respawn et garderait sinon l'intensite de charge.
			if (light != null)
				light.intensity = start_intensity;

			mirror.ForceBreakUpward(ExplodeUpwardBoost);
		}

		on_complete?.Invoke();
	}

	Light FindIndividualLight(MirrorActor mirror)
	{
		if (mirror == null)
			return null;

		Light[] lights = mirror.GetComponentsInChildren<Light>(true);
		for (int i = 0; i < lights.Length; i++)
		{
			if (lights[i] != null && lights[i].gameObject.name == IndividualLightName)
				return lights[i];
		}

		return null;
	}
}
