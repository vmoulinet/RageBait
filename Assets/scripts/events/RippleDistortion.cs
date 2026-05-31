using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

[Serializable, VolumeComponentMenu("Post-processing/Custom/RippleDistortion")]
[SupportedOnRenderPipeline(typeof(HDRenderPipelineAsset))]
public sealed class RippleDistortion : CustomPostProcessVolumeComponent, IPostProcessComponent
{
	// Center of the ripple in screen UV space (0-1). 0.5,0.5 = center of screen.
	public Vector2Parameter Center = new Vector2Parameter(new Vector2(0.5f, 0.5f));

	// Current radius of the wavefront in UV units (drive this from code over time).
	public ClampedFloatParameter Radius = new ClampedFloatParameter(0f, 0f, 2f);

	// How wide the distorted band around the wavefront is.
	public ClampedFloatParameter Width = new ClampedFloatParameter(0.1f, 0.001f, 0.5f);

	// Pixel displacement strength. 0 = no effect.
	public ClampedFloatParameter Amplitude = new ClampedFloatParameter(0f, 0f, 0.1f);

	// Number of ripples inside the band.
	public ClampedFloatParameter Frequency = new ClampedFloatParameter(30f, 1f, 120f);

	// Correct for screen aspect ratio so the ripple stays circular.
	public BoolParameter CorrectAspect = new BoolParameter(true);

	Material material;

	static class Uniforms
	{
		public static readonly int InputTexture = Shader.PropertyToID("_InputTexture");
		public static readonly int Center = Shader.PropertyToID("_Center");
		public static readonly int Radius = Shader.PropertyToID("_Radius");
		public static readonly int Width = Shader.PropertyToID("_Width");
		public static readonly int Amplitude = Shader.PropertyToID("_Amplitude");
		public static readonly int Frequency = Shader.PropertyToID("_Frequency");
		public static readonly int Aspect = Shader.PropertyToID("_Aspect");
	}

	const string kShaderName = "Hidden/Chase/RippleDistortion";

	// Run after the rest of the post-processing stack so it distorts the final image.
	public override CustomPostProcessInjectionPoint injectionPoint =>
		CustomPostProcessInjectionPoint.AfterPostProcess;

	public bool IsActive() => material != null && Amplitude.value > 0f && Radius.value > 0f;

	public override void Setup()
	{
		Shader shader = Shader.Find(kShaderName);
		if (shader != null)
			material = new Material(shader);
		else
			Debug.LogWarning("[ripple] Setup: shader '" + kShaderName + "' not found.");
	}

	public override void Render(CommandBuffer cmd, HDCamera camera, RTHandle source, RTHandle destination)
	{
		if (material == null)
			return;

		material.SetTexture(Uniforms.InputTexture, source);
		material.SetVector(Uniforms.Center, Center.value);
		material.SetFloat(Uniforms.Radius, Radius.value);
		material.SetFloat(Uniforms.Width, Width.value);
		material.SetFloat(Uniforms.Amplitude, Amplitude.value);
		material.SetFloat(Uniforms.Frequency, Frequency.value);
		material.SetFloat(Uniforms.Aspect, CorrectAspect.value ? (float)camera.actualWidth / camera.actualHeight : 1f);

		HDUtils.DrawFullScreen(cmd, material, destination, shaderPassId: 0);
	}

	public override void Cleanup()
	{
		CoreUtils.Destroy(material);
	}
}
