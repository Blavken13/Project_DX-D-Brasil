using UnityEngine;
using System.Collections.Generic;
[System.Serializable]
public class AnimationElem
{
    public string key;
    public string motion;
    public UnityEngine.AnimationClip _clipObj;
}

[System.Serializable]
public class AnimationElem3State
{
    public string key;
    public string begin;
    public string during;
    public string end;
    public UnityEngine.AnimationClip _clipObjBegin;
    public UnityEngine.AnimationClip _clipObjLooping;
    public UnityEngine.AnimationClip _clipObjEnd;
}

[System.Serializable]
public class AnimationElemWeighted
{
    public string key;
    public List<WeightedMotion> elems;
}

[System.Serializable]
public class WeightedMotion
{
    public float weight;
    public string motion;
    public Vector2 duration;
    public UnityEngine.AnimationClip _clipObj;
}

[System.Serializable]
public class AnimationElemWeighted3State
{
    public string key;
    public List<Weighted3StateMotion> elems;
}

[System.Serializable]
public class Weighted3StateMotion
{
    public float weight;
    public string begin;
    public List<WeightedMotion> loopings;
    public string end;
    public UnityEngine.AnimationClip _clipObjBegin;
    public UnityEngine.AnimationClip _clipObjEnd;
}

[System.Serializable]
public class AnimationElemMoveSet
{
    public string key;
    public List<MoveSet> elems;
}

[System.Serializable]
public class MoveSet
{
    public string name;
    public List<MoveMotionInfo> motions;
}

[System.Serializable]
public class MoveMotionInfo
{
    public string motion;
    public string rot_motion_cw;
    public string rot_motion_ccw;
    public string turn_reverse_motion;
    public Condition conditions;
    public float base_move_speed;
    public float rot_speed;
    public float playback_rate;
    public float rot_playback_rate;
    public UnityEngine.AnimationClip _clipObjMove;
    public UnityEngine.AnimationClip _clipObjRotateCW;
    public UnityEngine.AnimationClip _clipObjRotateCCW;
    public UnityEngine.AnimationClip _clipObjTurnReverse;
}

[System.Serializable]
public class Condition
{
    public string flag;
    public string enemy_distance_with_bound;
}

[System.Serializable]
public class AnimationElemDirectional
{
    public string key;
    public string front;
    public string back;
    public string left;
    public string right;
    public UnityEngine.AnimationClip _clipObjFront;
    public UnityEngine.AnimationClip _clipObjBack;
    public UnityEngine.AnimationClip _clipObjLeft;
    public UnityEngine.AnimationClip _clipObjRight;
}

[System.Serializable]
public class AnimationElemAttack
{
    public string key;
    public ActionMeta meta;
    public List<AttackInfo> attack_info;
    public bool SameAbove;
}

[System.Serializable]
public class ActionMeta
{
    public string motion;
    public bool bound_enemy;
    public float rot_speed;
    public UnityEngine.AnimationClip _clipObj;
}

[System.Serializable]
public class AttackInfo
{
    public int frame;
    public string sub_action_id;
    public int damage_type;
    public float radius;
    public float radius_min;
    public Vector2 angles;
    public Vector2 rect_half_size;
    public Vector2 offset;
    public float damage_angle;
    public bool use_target_origin;
}

[System.Serializable]
public class AnimationEventPair
{
    public string Name;
    public List<AnimationEventInfo> Infos;
}

[System.Serializable]
public class AnimationEventInfo
{
    public int frame;
    public float time;
    public int animEventCmd;
    public bool isOnce;
    public bool isFinally;
    public bool removeEnded;
    public string paramStr;
    public string paramStr2;
    public string paramStr3;
    public Vector3 paramVector;
    public Vector3 paramVector2;
    public float paramFloat;
    public int paramInt;
    public int paramInt2;
    public int enumID;
    public bool paramBool;
    public bool paramBool2;
    public bool shared;
    public string gameObjectPath;
}

[System.Serializable]
public class ParticleType
{
    public string Path;
}

[System.Serializable]
public class IntegratedEffectType
{
    public string Path;
}
