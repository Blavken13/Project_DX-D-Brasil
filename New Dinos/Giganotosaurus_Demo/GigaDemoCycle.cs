using UnityEngine;
// Demo only: alternates Idle (6s) and Walk (6s) in place so both clips can be seen. No gameplay.
[RequireComponent(typeof(Animator))]
public class GigaDemoCycle : MonoBehaviour
{
    public float period = 6f;
    Animator a; float t; bool walk;
    void Awake() { a = GetComponent<Animator>(); }
    void Update() { t += Time.deltaTime; if (t >= period) { t = 0; walk = !walk; a.SetBool("Walk", walk); } }
}
