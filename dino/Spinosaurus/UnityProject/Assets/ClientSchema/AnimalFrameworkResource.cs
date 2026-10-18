// Serialization schema extracted from the client. Authoring only.
using UnityEngine;
using System.Collections.Generic;
public class AnimalFrameworkResource : ScriptableObject
{
    public List<AnimationElem> normal_motions;
    public List<AnimationElem3State> normal_3states;
    public List<AnimationElemWeighted> weighted_motions;
    public List<AnimationElemWeighted3State> weighted_3states;
    public List<AnimationElemMoveSet> move_sets;
    public List<AnimationElem> comabt_motions;
    public List<AnimationElemDirectional> combat_directions;
    public List<AnimationElem3State> combat_3states;
    public List<AnimationElemAttack> combat_attacks;
    public List<AnimationElem> active_skills;
    public List<AnimationElem> etc_motions;
}
