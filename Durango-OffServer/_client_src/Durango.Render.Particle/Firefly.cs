using System.Collections;
using Durango.Terrain;
using Durango.Utils;
using JetBrains.Annotations;
using UnityEngine;

namespace Durango.Render.Particle;

public class Firefly : MonoBehaviour
{
	private const float StartTime = 5f / 6f;

	private const float EndTime = 5f / 24f;

	private const float RandomInterval = 1f / 24f;

	private int _particleId;

	private bool _isActiveTime;

	private static bool _particleAllowed;

	private IEnumerator Start()
	{
		while (true)
		{
			float normalizedTime = TimeGauge.GetNormalizedTime();
			float num = Random.value * 2f - 1f;
			normalizedTime += num * (1f / 24f);
			_isActiveTime = IsActiveTime(normalizedTime);
			UpdateParticle();
			float num2 = ((!_isActiveTime) ? (5f / 6f - normalizedTime) : (5f / 24f - normalizedTime));
			if (num2 < 0f)
			{
				num2 += 1f;
			}
			float realTimeFromNormalizedTime = TimeGauge.GetRealTimeFromNormalizedTime(num2);
			yield return new WaitForSeconds(realTimeFromNormalizedTime);
		}
	}

	private void EmitParticle()
	{
		if (_particleId == 0)
		{
			float num = Random.value - 0.5f;
			float num2 = Random.value - 0.5f;
			Vector3 pos = new Vector3(num * 200f, 0f, num2 * 200f);
			_particleId = ParticleManager.EmitFollow("Particle/FX_Prop_FireFly_01.prefab", pos, Quaternion.identity, base.transform, useLocalPosition: true, comeForwardToCamera: false, groundDecal: false, default(Vector3), null, reusable: true, limit: false);
		}
	}

	private void StopParticle()
	{
		if (_particleId != 0)
		{
			ParticleManager.Stop(_particleId, immediately: false);
			_particleId = 0;
		}
	}

	private static bool IsActiveTime(float normalizedTime)
	{
		if (!(5f / 6f < normalizedTime))
		{
			return normalizedTime < 5f / 24f;
		}
		return true;
	}

	private void OnDisable()
	{
		_isActiveTime = false;
		StopParticle();
	}

	private void UpdateParticle()
	{
		if (_isActiveTime && _particleAllowed)
		{
			EmitParticle();
		}
		else
		{
			StopParticle();
		}
	}

	public static void ChangeFireflyOption(bool allow)
	{
		_particleAllowed = allow;
		if (Singleton<TerrainBase>.HasInstance())
		{
			Singleton<TerrainBase>.Instance().gameObject.BroadcastMessage("OnFireflyOptionChanged", SendMessageOptions.DontRequireReceiver);
		}
	}

	[UsedImplicitly]
	private void OnFireflyOptionChanged()
	{
		UpdateParticle();
	}
}
