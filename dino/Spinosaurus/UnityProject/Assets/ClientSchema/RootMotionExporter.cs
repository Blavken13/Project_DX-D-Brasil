// Serialization schema extracted from the client. Authoring only.
using UnityEngine;
using System.Collections.Generic;
namespace Durango.Model
{
public class RootMotionExporter : MonoBehaviour
{
    public float _deadband;
    public float _deadbandYaw;
    public string _rootMotionBoneName;
    public Vector3 _rootMotionForward;
    public bool _doesImportAnimEventFilesFolder;
}
}
