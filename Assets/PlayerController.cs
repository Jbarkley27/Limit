using System.Collections;
using System.Collections.Generic;
using FMODUnity;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public float hoverHeight;
    public Rigidbody rb;

    public Transform nonRotatingContainer;

    public Transform rotatingContainer;

    public LayerMask groundMask;
    public LayerMask enemyMask;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        //Hover();

    }

    private void FixedUpdate()
    {
        Hover();
        Look();

        // Check for input (e.g., pressing a button to dash)
        if (InputManager.DashInput && canDash)
        {
            StartCoroutine(Dash());
        }

        // Check for input (e.g., pressing a button to jump)
        if (InputManager.JumpInput && canJump)
        {
            StartCoroutine(Jump());
        }
    }





    [Header("Jump")]
    public float jumpCooldown;
    public float jumpForce;
    public bool canJump = true;
    public bool jumping;
    public float jumpMass = 6;
    public float defaultMass = 1;
    public float jumpDrag;
    //public float stopHoverToJumpTime;

    IEnumerator Jump()
    {
        canHover = false;
        canJump = false;
        jumping = true;

        rb.AddForce(jumpForce * Vector3.up, ForceMode.Impulse);

        yield return new WaitForSeconds(jumpCooldown);

        canJump = true;
        jumping = false;


        InputManager.JumpInput = false;
    }




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


    public void Hover()
    {

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
        else
        {
            rb.mass = jumpMass;
            rb.useGravity = true;
            rb.drag = jumpDrag;
            IsGrounded = false;
            rb.AddForce(-nonRotatingContainer.up * gravityForce, ForceMode.Acceleration);
        }
    }





    [Header("Look At")]
    public float LookSpeed;
    public Transform debugObject;
    Vector3 targetDebugPosition;
    public GameObject enemyLocked;
    public GameObject lockOnUIIcon;
    public static Vector3 AimDirection;


    public float aimAssistRadius;
    RaycastHit hit;
    public void Look()
    {
        var ray = Camera.main.ScreenPointToRay(new Vector3(FreeCursor.reticleX, FreeCursor.reticleY, 0));

        if (Physics.SphereCast(transform.position, aimAssistRadius, ray.direction, out hit, enemyMask))
        {
            Debug.Log(hit.collider.gameObject);
        }

       

        if (Physics.Raycast(ray, out var hitInfo, Mathf.Infinity))
        {
            //Debug.Log("Htting Something -- " + hitInfo.transform.gameObject.name);
            debugObject.transform.position = hitInfo.point;

            targetDebugPosition = hitInfo.point;

            if (hitInfo.transform.gameObject.CompareTag("Enemy"))
            {
                enemyLocked = hitInfo.transform.gameObject;
            }
            else
            {
                enemyLocked = null;
            }
        }

        var direction = targetDebugPosition - rotatingContainer.position;
        AimDirection = direction;

        rotatingContainer.forward = direction;

        LockOnUI();
    }

    public void LockOnUI()
    {
        if (enemyLocked != null)
        {
            lockOnUIIcon.transform.position = Camera.main.WorldToScreenPoint(enemyLocked.transform.position);
            lockOnUIIcon.SetActive(true);
        }

        else
        {
            lockOnUIIcon.SetActive(false);
        }
    }
}
