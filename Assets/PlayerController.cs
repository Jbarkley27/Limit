using System.Collections;
using FMODUnity;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public Rigidbody rb;

    [Header("Y Rotate Parent")]
    public Transform nonRotatingContainer;

    [Header("X | Z Rotate Parent")]
    public Transform rotatingContainer;

    [Header("Layer Masks")]
    public LayerMask groundMask;
    public LayerMask enemyMask;


    void Update()
    {
        // Watch for Death
        if (HealthManager.instance.IsDead) HandleDeath();
    }

    private void FixedUpdate()
    {
        Hover();
        Look();

        // Watch for Dash Input
        if (InputManager.DashInput && canDash)
        {
            if (HealthManager.instance.IsDead) return;
            StartCoroutine(Dash());
        }

        // Watch for Jump Input
        //if (InputManager.JumpInput && canJump)
        //{
        //    if (HealthManager.instance.IsDead) return;
        //    StartCoroutine(Jump());
        //}
    }





    //[Header("Jump")]
    //public float jumpCooldown;
    //public float jumpForce;
    //private bool canJump = true;
    //private bool jumping;
    //public float jumpMass = 6;
    
    //public float jumpDrag;

    //IEnumerator Jump()
    //{
    //    canHover = false;
    //    canJump = false;
    //    jumping = true;

    //    rb.AddForce(jumpForce * Vector3.up, ForceMode.Impulse);

    //    yield return new WaitForSeconds(jumpCooldown);

    //    canJump = true;
    //    jumping = false;


    //    InputManager.JumpInput = false;
    //}




    [Header("Dash")]
    public float dashDistance = 5f;
    public float dashTime = 0.5f;
    public float cooldownTime = 2f;
    public float dashForce;

    public bool canDash = true;

    IEnumerator Dash()
    {
        // Disable further dashes during cooldown
        canDash = false;

        RuntimeManager.PlayOneShot(AudioLibrary.Test, transform.position);

        ScreenShakeManager.Instance.DoShake(ScreenShakeManager.DashProfile);

        rb.AddForce(new Vector3(InputManager.MovementInput.x, 0, InputManager.MovementInput.y) * dashForce, ForceMode.VelocityChange);


        yield return new WaitForSeconds(dashTime);

        // Stop the velocity manually
        rb.velocity = Vector3.zero;

        // Enable dashes after cooldownTime
        yield return new WaitForSeconds(cooldownTime);
        InputManager.DashInput = false;
        canDash = true;
    }






    [Header("Hover/Movement")]
    public float hoverSpeed;
    public float movementSpeed;
    public bool IsGrounded;
    public float distanceCheck;
    public float scopeDistance;
    RaycastHit hoverHeightHit;
    public float hoverForceMultiplier;
    public float gravityForce;
    public bool canHover = true;
    public float defaultMass = 1;


    public void Hover()
    {
        if (HealthManager.instance.IsDead) return;

        // Movement
        rb.AddForce(new Vector3(InputManager.MovementInput.x, 0, InputManager.MovementInput.y) * Time.deltaTime * movementSpeed);


        //if (!canHover) return;
        // Hover
        Debug.DrawRay(rotatingContainer.position, rotatingContainer.forward * distanceCheck, Color.red);

        if (Physics.Raycast(nonRotatingContainer.position, -nonRotatingContainer.up, out hoverHeightHit, distanceCheck, groundMask))
        {

            IsGrounded = true;

            // Calculate the distance from the ground
            float distanceToGround = hoverHeightHit.distance;

            // Calculate the hover error
            float hoverError = distanceCheck - distanceToGround;

            float hoverF = Mathf.Abs(1 / (hoverHeightHit.point.y - nonRotatingContainer.position.y)) * hoverError;

            // Calculate the target position above the ground
            Vector3 targetPosition = new Vector3(transform.position.x, hoverHeightHit.point.y + distanceCheck, transform.position.z);

            // Smoothly move the rigidbody to the target position using MovePosition
            rb.MovePosition(Vector3.Lerp(transform.position, targetPosition, Time.fixedDeltaTime * hoverF * hoverForceMultiplier));
            rb.useGravity = false;
            rb.drag = 3;
            rb.mass = defaultMass;
        }
        //else
        //{
        //    rb.mass = jumpMass;
        //    rb.useGravity = true;
        //    rb.drag = jumpDrag;
        //    IsGrounded = false;
        //    rb.AddForce(-nonRotatingContainer.up * gravityForce, ForceMode.Acceleration);
        //}
    }





    // Death
    public bool deathTriggered = false;
    public void HandleDeath()
    {
        if (deathTriggered) return;

        StartCoroutine(Die());
    }

    IEnumerator Die()
    {
        deathTriggered = true;
        // play death animation
        // remove rb constraints so enemy can fall and roll naturally
        rb.constraints = RigidbodyConstraints.None;
        rb.useGravity = true;
        yield return new WaitForSeconds(1);
    }

    


    [Header("Look At")]
    public float LookSpeed;
    Vector3 targetDebugPosition;
    public GameObject aimingAtEnemy;
    public static Vector3 AimDirection;
    public GameObject ringProjection;
    public LayerMask aimMask;

    public void Look()
    {
        var ray = Camera.main.ScreenPointToRay(new Vector3(FreeCursor.reticleX, FreeCursor.reticleY, 0));


        if (Physics.Raycast(ray, out var hitInfo, Mathf.Infinity, aimMask))
        {
            // Ensure the player isn't used in the rotation
            targetDebugPosition = hitInfo.point;


            if (hitInfo.transform.gameObject.CompareTag("Enemy"))
            {
                aimingAtEnemy = hitInfo.transform.gameObject;
            }
            else
            {
                aimingAtEnemy = null;
            }
        }

        Debug.DrawRay(transform.position, transform.forward * 1000f, Color.red);
        AimDirection = targetDebugPosition - rotatingContainer.position;
        AimDirection.y = 0;
        rotatingContainer.forward = AimDirection;

        HandleRingProjection();
    }

    public void HandleRingProjection()
    {
        Quaternion newDirection = rotatingContainer.transform.rotation;

        newDirection.x = 0;
        newDirection.z = 0;

        ringProjection.transform.rotation = newDirection;
    }

    
}
