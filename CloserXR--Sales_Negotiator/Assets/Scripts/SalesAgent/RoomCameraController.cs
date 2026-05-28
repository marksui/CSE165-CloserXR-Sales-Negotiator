using UnityEngine;

namespace CloserXR.SalesNegotiator
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class RoomCameraController : MonoBehaviour
    {
        [SerializeField] private bool disableOnAndroid = true;
        [SerializeField] private bool requireRightMouseButtonToLook = true;
        [SerializeField] private float mouseSensitivity = 2.4f;
        [SerializeField] private float moveSpeed = 2.2f;
        [SerializeField] private float verticalMoveSpeed = 1.5f;
        [SerializeField] private float fastMoveMultiplier = 2.75f;
        [SerializeField] private Vector2 pitchLimits = new Vector2(-78f, 78f);

        private float yaw;
        private float pitch;
        private bool cursorCaptured;
        private bool previousCursorVisible;
        private CursorLockMode previousCursorLockState;

        private void OnEnable()
        {
            SyncAnglesFromTransform();
        }

        private void OnDisable()
        {
            ReleaseCursor();
        }

        private void Update()
        {
            if (disableOnAndroid && Application.platform == RuntimePlatform.Android)
            {
                return;
            }

            UpdateLook();
            UpdateMove();
        }

        private void UpdateLook()
        {
            bool canLook = !requireRightMouseButtonToLook || Input.GetMouseButton(1);
            if (requireRightMouseButtonToLook)
            {
                if (Input.GetMouseButtonDown(1))
                {
                    CaptureCursor();
                }
                else if (Input.GetMouseButtonUp(1))
                {
                    ReleaseCursor();
                }
            }

            if (!canLook)
            {
                return;
            }

            yaw += Input.GetAxisRaw("Mouse X") * mouseSensitivity;
            pitch -= Input.GetAxisRaw("Mouse Y") * mouseSensitivity;
            pitch = Mathf.Clamp(pitch, Mathf.Min(pitchLimits.x, pitchLimits.y), Mathf.Max(pitchLimits.x, pitchLimits.y));
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        private void UpdateMove()
        {
            Vector3 input = Vector3.zero;
            if (Input.GetKey(KeyCode.A))
            {
                input.x -= 1f;
            }

            if (Input.GetKey(KeyCode.D))
            {
                input.x += 1f;
            }

            if (Input.GetKey(KeyCode.W))
            {
                input.z += 1f;
            }

            if (Input.GetKey(KeyCode.S))
            {
                input.z -= 1f;
            }

            if (Input.GetKey(KeyCode.E))
            {
                input.y += 1f;
            }

            if (Input.GetKey(KeyCode.Q))
            {
                input.y -= 1f;
            }

            if (input.sqrMagnitude < 0.001f)
            {
                return;
            }

            if (input.sqrMagnitude > 1f)
            {
                input.Normalize();
            }

            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.forward;
            }

            Vector3 right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
            if (right.sqrMagnitude < 0.001f)
            {
                right = Vector3.right;
            }

            float speed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                ? moveSpeed * fastMoveMultiplier
                : moveSpeed;
            Vector3 horizontalMove = (right * input.x + forward * input.z) * speed;
            Vector3 verticalMove = Vector3.up * input.y * verticalMoveSpeed;
            transform.position += (horizontalMove + verticalMove) * Time.deltaTime;
        }

        private void SyncAnglesFromTransform()
        {
            Vector3 eulerAngles = transform.rotation.eulerAngles;
            yaw = eulerAngles.y;
            pitch = NormalizeAngle(eulerAngles.x);
            pitch = Mathf.Clamp(pitch, Mathf.Min(pitchLimits.x, pitchLimits.y), Mathf.Max(pitchLimits.x, pitchLimits.y));
        }

        private void CaptureCursor()
        {
            if (cursorCaptured)
            {
                return;
            }

            previousCursorVisible = Cursor.visible;
            previousCursorLockState = Cursor.lockState;
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
            cursorCaptured = true;
        }

        private void ReleaseCursor()
        {
            if (!cursorCaptured)
            {
                return;
            }

            Cursor.visible = previousCursorVisible;
            Cursor.lockState = previousCursorLockState;
            cursorCaptured = false;
        }

        private static float NormalizeAngle(float angle)
        {
            while (angle > 180f)
            {
                angle -= 360f;
            }

            while (angle < -180f)
            {
                angle += 360f;
            }

            return angle;
        }
    }
}
