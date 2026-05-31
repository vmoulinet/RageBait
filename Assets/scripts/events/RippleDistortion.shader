Shader "Hidden/Chase/RippleDistortion"
{
	HLSLINCLUDE

	#pragma target 4.5
	#pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch

	#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

	// Unique names: CustomPassCommon.hlsl already declares Attributes/Varyings/Vert.
	struct RippleAttributes
	{
		uint vertexID : SV_VertexID;
		UNITY_VERTEX_INPUT_INSTANCE_ID
	};

	struct RippleVaryings
	{
		float4 positionCS : SV_POSITION;
		float2 texcoord   : TEXCOORD0;
		UNITY_VERTEX_OUTPUT_STEREO
	};

	RippleVaryings VertRipple(RippleAttributes input)
	{
		RippleVaryings output;
		UNITY_SETUP_INSTANCE_ID(input);
		UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
		output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
		output.texcoord   = GetFullScreenTriangleTexCoord(input.vertexID);
		return output;
	}

	TEXTURE2D_X(_InputTexture);

	float2 _Center;
	float  _Radius;
	float  _Width;
	float  _Amplitude;
	float  _Frequency;
	float  _Aspect;

	float4 FragRipple(RippleVaryings input) : SV_Target
	{
		UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

		float2 uv = input.texcoord;

		// Aspect-corrected distance from the ripple center so the wavefront is circular.
		float2 diff = uv - _Center;
		diff.x *= _Aspect;
		float dist = length(diff);

		// Distance from the current wavefront radius.
		float d = dist - _Radius;

		// Smooth falloff so the band fades at its edges (no hard ring).
		float falloff = 1.0 - smoothstep(0.0, _Width, abs(d));

		// Sine wave riding the wavefront, pushed along the direction from center.
		float wave = sin(d * _Frequency) * falloff * _Amplitude;

		float2 dir = (dist > 1e-5) ? (diff / dist) : float2(0.0, 0.0);
		// undo aspect on the displacement direction
		dir.x /= _Aspect;

		float2 distortedUV = uv + dir * wave;
		distortedUV = clamp(distortedUV, 0.0, 1.0);

		uint2 pixel = uint2(distortedUV * _ScreenSize.xy);
		float3 color = LOAD_TEXTURE2D_X(_InputTexture, pixel).rgb;

		return float4(color, 1.0);
	}

	ENDHLSL

	SubShader
	{
		Tags { "RenderPipeline" = "HDRenderPipeline" }
		Pass
		{
			Name "RippleDistortion"
			ZWrite Off
			ZTest Always
			Blend Off
			Cull Off

			HLSLPROGRAM
				#pragma vertex VertRipple
				#pragma fragment FragRipple
			ENDHLSL
		}
	}
	Fallback Off
}
