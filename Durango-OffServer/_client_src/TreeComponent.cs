using System;
using System.Collections;
using Durango.Render.Camera;
using Durango.Render.Particle;
using Durango.Render.Sprite;
using Durango.Utils;
using UnityEngine;

public class TreeComponent : NaturalComponent
{
	private const float FallenAngle = 45f;

	private const float Bouncing1Angle = 0.5f;

	private const float Bouncing2Angle = -0.2f;

	private const float CurveFactor = 6f;

	private const float Bouncing1Time = 4f;

	private const float Bouncing2Time = 4.2f;

	private const float Bouncing3Time = 4.3f;

	private const float FadingOutTime = 6f;

	private const string TreeFellingSound = "Prop_tree_felling_01";

	private const string TreeBouncingSound = "Prop_tree_fallground_01";

	private const float ParticleEmitHeight = 300f;

	public float SpriteHeight = 10f;

	private Durango.Render.Sprite.Sprite _stumpSprite;

	public TreeComponent(NaturalSpriteObject natural)
		: base(natural)
	{
		SoundManager.PrepareEvent("Prop_tree_felling_01");
		SoundManager.PrepareEvent("Prop_tree_fallground_01");
	}

	public void OnLoot()
	{
		if (base.GameObject.activeSelf)
		{
			ParticleManager.Emit("Particle/Tree_Crash_01.prefab", base.Position + new Vector3(0f, 300f, 0f), Quaternion.identity, comeForwardToCamera: true);
			SoundManager.PlayEvent("Prop_tree_felling_01", SoundPosition.Fix(base.Position));
			AddStump();
			base.Natural.StartCoroutine(CoLoot());
		}
	}

	private IEnumerator CoLoot()
	{
		float startFellingTime = Time.realtimeSinceStartup;
		GameObject tree = base.Sprite.GameObject;
		float num;
		while ((num = Time.realtimeSinceStartup - startFellingTime) < 4f)
		{
			float alpha = 1f - num / 6f;
			base.Sprite.SetAlpha(alpha);
			float f = num / 4f;
			f = Mathf.Pow(f, 6f);
			if (f > 1f)
			{
				f = 1f;
			}
			tree.transform.localRotation = Quaternion.Euler(0f, 45f, -45.5f * f);
			yield return null;
		}
		SoundManager.PlayEvent("Prop_tree_fallground_01", SoundPosition.Fix(base.Position));
		float num2 = Mathf.Sin((float)Math.PI / 4f) * 300f;
		Vector3 vector = new Vector3(num2 * Mathf.Cos((float)Math.PI / 4f), Mathf.Cos((float)Math.PI / 4f) * 300f, (0f - num2) * Mathf.Cos((float)Math.PI / 4f));
		vector += Singleton<MainCamera>.Instance().transform.forward * 500f;
		ParticleManager.Emit("Particle/FX_Prop_Tree_Fallground_01.prefab", rotation: Quaternion.Euler(270f, 180f, 0f), pos: base.Position + vector, comeForwardToCamera: true);
		float num3;
		while ((num3 = Time.realtimeSinceStartup - startFellingTime) < 4.2f)
		{
			float alpha2 = 1f - num3 / 6f;
			base.Sprite.SetAlpha(alpha2);
			float num4 = (num3 - 4f) / 0.19999981f;
			tree.transform.localRotation = Quaternion.Euler(0f, 45f, 0f - (45f + 0.5f * (1f - num4) + -0.2f * num4));
			yield return null;
		}
		float num5;
		while ((num5 = Time.realtimeSinceStartup - startFellingTime) < 4.3f)
		{
			float alpha3 = 1f - num5 / 6f;
			base.Sprite.SetAlpha(alpha3);
			float num6 = (num5 - 4.2f) / 0.10000038f;
			tree.transform.localRotation = Quaternion.Euler(0f, 45f, 0f - (45f + -0.2f * (1f - num6)));
			yield return null;
		}
		tree.transform.localRotation = Quaternion.Euler(0f, 45f, -45f);
		float num7;
		while ((num7 = Time.realtimeSinceStartup - startFellingTime) < 6f)
		{
			float alpha4 = 1f - num7 / 6f;
			base.Sprite.SetAlpha(alpha4);
			yield return null;
		}
		base.Sprite.SetAlpha(0f);
		RemoveStump();
	}

	private void AddStump()
	{
		if (!string.IsNullOrEmpty(base.Sprite.StumpName) && _stumpSprite == null)
		{
			_stumpSprite = Singleton<SpriteManager>.Instance().CreateSprite(SpriteObjectType.Shrub, base.Sprite.StumpName);
			_stumpSprite.GameObject.name = "Stump";
			_stumpSprite.GameObject.transform.position = base.Sprite.GameObject.transform.position + new Vector3(0f, 0f, 0.1f);
			_stumpSprite.GameObject.transform.rotation = base.Sprite.GameObject.transform.rotation;
			_stumpSprite.GameObject.transform.localScale = Vector3.one;
		}
	}

	private void RemoveStump()
	{
		base.Natural.StartCoroutine(CoStumpFadeOut());
	}

	private IEnumerator CoStumpFadeOut()
	{
		if (_stumpSprite == null)
		{
			base.GameObject.SetActive(value: false);
			yield break;
		}
		float remainTime = 2f;
		while (remainTime >= 0f)
		{
			remainTime -= Time.deltaTime;
			float alpha = Mathf.Clamp01(remainTime / 2f);
			_stumpSprite.SetAlpha(alpha);
			yield return null;
		}
		UnityEngine.Object.Destroy(_stumpSprite.GameObject);
		_stumpSprite = null;
		base.GameObject.SetActive(value: false);
	}

	public void BeginShake(bool emitParticle)
	{
		if (emitParticle)
		{
			ParticleManager.Emit("Particle/LeafParticle.prefab", base.Position + new Vector3(0f, SpriteHeight, 0f), Quaternion.identity, comeForwardToCamera: true);
		}
	}
}
