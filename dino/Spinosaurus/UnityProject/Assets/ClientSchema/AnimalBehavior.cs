// Serialization schema extracted from the client. Authoring only.
using UnityEngine;
using System.Collections.Generic;
public class AnimalBehavior : MonoBehaviour
{
    public float _xRadius;
    public float _yRadius;
    public int _size;
    public float _bushWhackEffectPeriod;
    public Vector3 _bushWhackEffectOffset;
    public IntegratedEffectType _hitEffect;
    public AnimalFrameworkResource AnimalFrameworkResource;
    public string _bodyPartHead;
    public string _bodyPartBody;
    public string _bodyPartArm;
    public string _bodyPartArmR;
    public string _bodyPartLeg;
    public string _bodyPartLegR;
    public string _bodyPartTail;
    public string _bodyPartBack;
    public string _bodyPartBip001;
    public bool _processDepth;
    public List<UnityEngine.Renderer> _closedEyes;
    public List<UnityEngine.GameObject> _nightEyes;
    public List<UnityEngine.GameObject> _eyeTrail;
    public UnityEngine.Texture2D _dieTexture;
    public float UIViewScale;
    public float _lootableRimMax;
    public float _lootableRimMin;
    public float _lootableRimIntensity;
    public UnityEngine.GameObject _meshObject;
}
