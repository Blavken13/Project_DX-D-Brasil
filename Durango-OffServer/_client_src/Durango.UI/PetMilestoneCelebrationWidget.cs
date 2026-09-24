using System.Collections;
using System.Collections.Generic;
using Durango.Render.Particle;
using Durango.Utils;
using UnityEngine;

namespace Durango.UI;

public class PetMilestoneCelebrationWidget : MonoBehaviour
{
	[SerializeField]
	private ParticleType _startParticle;

	[SerializeField]
	private ParticleType _loopParticle;

	[SerializeField]
	private Transform[] _startParticlePositions;

	[SerializeField]
	private Transform[] _loopParticlePositions;

	[SerializeField]
	private float _startParticleDelay;

	[SerializeField]
	private float _loopParticleDelay;

	private ICoroutineBinder _binder;

	private bool _isParticleActivated;

	private readonly List<int> _particles = new List<int>();

	private void OnDisable()
	{
		HideParticles();
	}

	private void Update()
	{
		if (!_isParticleActivated)
		{
			ShowParticles();
		}
	}

	private void ShowParticles()
	{
		_isParticleActivated = true;
		this.StartCoroutine(ref _binder, CoShowParticles());
	}

	private IEnumerator CoShowParticles()
	{
		if (_startParticleDelay > 0f)
		{
			yield return new WaitForSeconds(_startParticleDelay);
		}
		int i = 0;
		for (int size = KUtility.GetSize(_startParticlePositions); i < size; i++)
		{
			string path = _startParticle.Path;
			Vector3 zero = Vector3.zero;
			Quaternion identity = Quaternion.identity;
			Transform followingParent = _startParticlePositions[i];
			Vector3 one = Vector3.one;
			int item = ParticleManager.EmitFollow(path, zero, identity, followingParent, useLocalPosition: true, comeForwardToCamera: false, groundDecal: false, one);
			_particles.Add(item);
		}
		if (_loopParticleDelay > 0f)
		{
			yield return new WaitForSeconds(_loopParticleDelay);
		}
		int j = 0;
		for (int size2 = KUtility.GetSize(_loopParticlePositions); j < size2; j++)
		{
			string path2 = _loopParticle.Path;
			Vector3 zero2 = Vector3.zero;
			Quaternion identity2 = Quaternion.identity;
			Transform followingParent2 = _loopParticlePositions[j];
			Vector3 one2 = Vector3.one;
			int item2 = ParticleManager.EmitFollow(path2, zero2, identity2, followingParent2, useLocalPosition: true, comeForwardToCamera: false, groundDecal: false, one2);
			_particles.Add(item2);
		}
	}

	private void HideParticles()
	{
		_isParticleActivated = false;
		for (int i = 0; i < _particles.Count; i++)
		{
			ParticleManager.Stop(_particles[i]);
		}
		_particles.Clear();
	}
}
