using UnityEngine;

// Authored child meshes live in Assets/Static/ElevatorPersonas. This component only poses them.
public sealed class ElevatorPersonaRig : MonoBehaviour
{
    public Transform candyHand, rustyHand, candyArm, rustyArm;
    public Transform face, leftEye, rightEye, smile, frown, leftBrow, rightBrow;
    public GameObject candyDecor, rustyDecor, candyBackdrop, waterBackdrop;
    public Renderer facePlate;
    public Material candyFace, rustyFace;
    bool friendly = true, repaired;
    float clock;
    Vector3 faceHome;

    void Awake() { faceHome = face.localPosition; RestHands(); }

    public void SetWorld(bool isFriendly, bool isRepaired)
    {
        friendly = isFriendly; repaired = isRepaired;
        candyDecor.SetActive(friendly); rustyDecor.SetActive(!friendly);
        candyBackdrop.SetActive(friendly); waterBackdrop.SetActive(!friendly);
        candyHand.gameObject.SetActive(friendly); candyArm.gameObject.SetActive(friendly);
        rustyHand.gameObject.SetActive(!friendly); rustyArm.gameObject.SetActive(!friendly);
        facePlate.sharedMaterial = friendly ? candyFace : rustyFace;
        smile.gameObject.SetActive(friendly); frown.gameObject.SetActive(!friendly);
        leftBrow.localRotation = Quaternion.Euler(0, 0, friendly ? 8 : -23);
        rightBrow.localRotation = Quaternion.Euler(0, 0, friendly ? -8 : 23);
    }

    public void Tick(float dt, bool moving, bool busy)
    {
        clock += dt;
        float cough = !friendly && !repaired && clock % 4.5f < .5f ? Mathf.Sin(clock * 48) * .025f : 0;
        face.localPosition = faceHome + Vector3.up * cough;
        face.localRotation = Quaternion.Euler(0, 0, cough * 90);
        float blink = clock % 3.8f < .13f ? .15f : friendly ? 1 : .65f;
        leftEye.localScale = rightEye.localScale = new Vector3(.12f, .16f * blink, .045f);
        if (!busy)
        {
            RestHands();
            var hand = friendly ? candyHand : rustyHand;
            hand.localPosition += Vector3.up * (friendly ? Mathf.Sin(clock * 1.6f) * .06f : cough);
            PoseArm(hand, friendly ? candyArm : rustyArm, friendly);
        }
    }

    public void RestHands()
    {
        candyHand.localPosition = new Vector3(-1.85f, 1.15f, 1.2f);
        rustyHand.localPosition = new Vector3(1.85f, 1.15f, 1.2f);
        candyHand.localRotation = Quaternion.identity;
        rustyHand.localRotation = Quaternion.identity;
        PoseArm(candyHand, candyArm, true); PoseArm(rustyHand, rustyArm, false);
    }

    public void GuidePassenger(Vector3 position, float progress, bool gentle, bool boarding)
    {
        // Use the hand that exists in this world; gentle controls the motion, not the art.
        var hand = friendly ? candyHand : rustyHand;
        var arm = friendly ? candyArm : rustyArm;
        // A soft palm supports the passenger; the piston pushes from behind.
        Vector3 contact = position + (gentle ? new Vector3(-.3f, .12f, 0)
            : new Vector3(friendly ? -.2f : 0, .65f, boarding ? -.45f : .45f));
        Vector3 rest = new Vector3(friendly ? -1.85f : 1.85f, 1.15f, 1.2f);
        float reach = Mathf.SmoothStep(0, 1, Mathf.Clamp01(progress / .13f));
        float retract = Mathf.SmoothStep(0, 1, Mathf.Clamp01((progress - .85f) / .15f));
        hand.localPosition = Vector3.Lerp(Vector3.Lerp(rest, contact, reach), rest, retract);
        hand.localRotation = Quaternion.Euler(gentle ? 0 : 80, 0, gentle ? -8 : 0);
        PoseArm(hand, arm, friendly);
    }

    void PoseArm(Transform hand, Transform arm, bool gentle)
    {
        Vector3 anchor = new Vector3(gentle ? -2.23f : 2.23f, 1.3f, .5f);
        Vector3 delta = hand.localPosition - anchor;
        arm.localPosition = (hand.localPosition + anchor) * .5f;
        arm.localRotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
        arm.localScale = new Vector3(gentle ? .16f : .23f, delta.magnitude * .5f, gentle ? .16f : .23f);
    }

    // Deterministic animation curves shared by boarding, exiting and the editor checks.
    public static void PassengerPose(Vector3 start, Vector3 end, float progress, bool gentle,
        out Vector3 position, out float lean, out float squash)
    {
        float t = Mathf.Clamp01(progress);
        if (gentle)
        {
            float glide = Mathf.SmoothStep(0, 1, t);
            position = Vector3.Lerp(start, end, glide) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * .23f);
            lean = Mathf.Sin(t * Mathf.PI * 2) * 4;
            squash = 1f;
        }
        else
        {
            float launch = Mathf.Clamp01((t - .18f) / .55f);
            float travel = 1 - Mathf.Pow(1 - launch, 3);
            Vector3 direction = (end - start).normalized;
            position = t < .18f ? start - direction * (Mathf.Sin(t / .18f * Mathf.PI) * .12f)
                : Vector3.Lerp(start, end, travel) + Vector3.up * (Mathf.Sin(launch * Mathf.PI) * .48f);
            lean = Mathf.Sin(launch * Mathf.PI) * (end.z > start.z ? -22 : 22);
            squash = t < .18f ? 1 - .18f * Mathf.Sin(t / .18f * Mathf.PI) : 1 + .1f * Mathf.Sin(launch * Mathf.PI);
        }
        if (t >= 1) { position = end; lean = 0; squash = 1; }
    }
}
