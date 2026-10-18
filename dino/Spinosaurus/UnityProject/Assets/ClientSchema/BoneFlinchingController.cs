// Serialization schema extracted from the client. Authoring only.
using UnityEngine;
using System.Collections.Generic;
namespace Durango.Model
{
public class BoneFlinchingController : MonoBehaviour
{
    public bool _isLimbCanceling;
    public int _numIter;
    public float _cullDistance;
    public float _flinchDeg;
    public float _flinchDuration;
}
}
