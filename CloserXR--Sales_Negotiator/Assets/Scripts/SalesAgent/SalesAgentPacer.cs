using UnityEngine;

namespace CloserXR.SalesNegotiator
{
    [DisallowMultipleComponent]
    public sealed class SalesAgentPacer : MonoBehaviour
    {
        [SerializeField] private SalesAgentAnimator agentAnimator;
        [SerializeField] private Transform userHead;
        [SerializeField] private float neutralDistance = 2.2f;
        [SerializeField] private float closingDistance = 1.35f;
        [SerializeField] private float defensiveDistance = 2.7f;
        [SerializeField] private float pacingWidth = 0.75f;
        [SerializeField] private float moveSpeed = 1.5f;
        [SerializeField] private float turnSpeed = 8f;
        [SerializeField] private bool respectRoomBounds = true;
        [SerializeField] private float roomWallPadding = 0.35f;

        private SpatialRoomMapDemo roomMap;

        private float desiredDistance;
        private float desiredWidth;
        private float paceTimer;
        private bool active;

        private void Awake()
        {
            if (agentAnimator == null)
            {
                agentAnimator = GetComponent<SalesAgentAnimator>();
            }

            desiredDistance = neutralDistance;
        }

        public void Assign(SalesAgentAnimator animator, Transform head)
        {
            agentAnimator = animator;
            userHead = head;
        }

        public void AssignRoomMap(SpatialRoomMapDemo map)
        {
            roomMap = map;
        }

        public void SetIntent(SalesIntent intent)
        {
            active = true;

            switch (intent)
            {
                case SalesIntent.PricePushback:
                case SalesIntent.Rejection:
                    desiredDistance = defensiveDistance;
                    desiredWidth = pacingWidth * 0.35f;
                    break;
                case SalesIntent.Agreement:
                case SalesIntent.Closing:
                    desiredDistance = closingDistance;
                    desiredWidth = pacingWidth * 0.25f;
                    break;
                default:
                    desiredDistance = neutralDistance;
                    desiredWidth = pacingWidth;
                    break;
            }
        }

        public void GoIdle()
        {
            active = false;
            desiredDistance = neutralDistance;
            desiredWidth = 0f;
            agentAnimator?.SetWalking(false);
        }

        private void Update()
        {
            if (userHead == null && Camera.main != null)
            {
                userHead = Camera.main.transform;
            }

            if (userHead == null)
            {
                return;
            }

            paceTimer += Time.deltaTime;
            Vector3 forward = Vector3.ProjectOnPlane(userHead.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f)
            {
                forward = Vector3.forward;
            }

            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            float sideOffset = active ? Mathf.Sin(paceTimer * 1.35f) * desiredWidth : 0f;
            Vector3 target = userHead.position + forward * desiredDistance + right * sideOffset;
            target.y = 0f;

            if (respectRoomBounds)
            {
                if (roomMap == null)
                {
                    roomMap = FindObjectOfType<SpatialRoomMapDemo>();
                }

                if (roomMap != null)
                {
                    target = roomMap.ClampToRoom(target, roomWallPadding);
                }
            }

            Vector3 before = transform.position;
            transform.position = Vector3.MoveTowards(transform.position, target, moveSpeed * Time.deltaTime);

            Vector3 lookDirection = Vector3.ProjectOnPlane(userHead.position - transform.position, Vector3.up);
            if (lookDirection.sqrMagnitude > 0.01f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
            }

            bool isMoving = active && Vector3.Distance(before, transform.position) > 0.002f;
            agentAnimator?.SetWalking(isMoving);
        }
    }
}
