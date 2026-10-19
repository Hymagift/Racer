using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class DriftCar : MonoBehaviour
{
    public float forwardSpeed = 15f;
    public float turnSpeed = 90f;
    public float steerSmooth = 8f;
    [Range(0f, 1f)]
    public float grip = 0.1f;

    Rigidbody rb;
    float steer, targetSteer;

    void Awake() => rb = GetComponent<Rigidbody>();

    void Update()
    {
        targetSteer = 0f;

        // Pointer = souris dans l'éditeur ET doigt sur téléphone
        var pointer = Pointer.current;
        if (pointer != null && pointer.press.isPressed)
        {
            float x = pointer.position.ReadValue().x;
            targetSteer = x < Screen.width / 2f ? -1f : 1f;
        }

        steer = Mathf.Lerp(steer, targetSteer, steerSmooth * Time.deltaTime);
    }

    void FixedUpdate()
    {
        float angle = steer * turnSpeed * Time.fixedDeltaTime;
        rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, angle, 0f));

        Vector3 v = rb.linearVelocity;
        Vector3 target = transform.forward * forwardSpeed;
        Vector3 newVel = Vector3.Lerp(v, target, grip);
        newVel.y = v.y;
        rb.linearVelocity = newVel;
    }
}