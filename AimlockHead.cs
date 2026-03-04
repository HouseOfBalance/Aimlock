using UnityEngine;
using UnityEngine.EventSystems;

// Script này gộp cả UI và Logic ngắm. Yêu cầu GẮN LÊN UI BUTTON (Image)
public class FreeFireAimController : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [Header("=== CÀI ĐẶT NÚT BẮN (UI) ===")]
    public float dragRadius = 150f; 
    public bool isFiring { get; private set; } 
    private Vector2 startTouchPos;
    private Vector2 currentJoyVector;

    [Header("=== TARGET TRANSFORMS ===")]
    public Transform cameraTransform; // Kéo Camera của nhân vật vào đây
    public LayerMask enemyLayer;

    [Header("=== BASE AIM SETTINGS ===")]
    public float baseSensitivity = 100f;
    public float dynamicBoost = 50f;
    public float maxSensitivity = 300f;
    public AnimationCurve responseCurve;
    public float horizontalBias = 1f;

    [Header("=== BOUNDS & STATE ===")]
    public float minPitch = -85f;
    public float maxPitch = 85f;
    public float pitch;

    [Header("=== FREE FIRE AIM LOCK ===")]
    public float snapUpTrigger = 0.45f;        
    public float bodyOffset = 1.2f;            
    public float aimSmoothSpeed = 15f;         
    public float unlockDownTrigger = -0.6f;    
    
    [Header("=== YAW SOFT MAGNET ===")]
    public float yawMagnetStrength = 8f;
    public float yawMagnetFOV = 60f;
    public float maxYawCorrectionSpeed = 180f;

    [Header("=== TRUE AIMBOT MODE (Toggle) ===")]
    public bool aimbotActive = false;
    public float aimbotFOV = 45f;
    public float aimbotSnapSpeed = 20f;
    public float aimbotRange = 80f;

    // Các biến trạng thái nội bộ
    private bool headLocked = false;
    private float currentPitch = 0f;
    private Transform currentTargetHead = null;
    private Transform lockedEnemyHead = null;

    private void Start()
    {
        // Khởi tạo góc nhìn ban đầu dựa theo camera hiện tại
        if (cameraTransform != null)
        {
            currentPitch = cameraTransform.localEulerAngles.x;
            if (currentPitch > 180f) currentPitch -= 360f;
        }
    }

    // ==========================================
    // NHẬN DIỆN CẢM ỨNG TỪ UI
    // ==========================================
    public void OnPointerDown(PointerEventData eventData)
    {
        isFiring = true;
        startTouchPos = eventData.position;
        currentJoyVector = Vector2.zero; 
    }

    public void OnDrag(PointerEventData eventData)
    {
        Vector2 dragDelta = eventData.position - startTouchPos;
        currentJoyVector = dragDelta / dragRadius;
        currentJoyVector.x = Mathf.Clamp(currentJoyVector.x, -1f, 1f);
        currentJoyVector.y = Mathf.Clamp(currentJoyVector.y, -1f, 1f);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isFiring = false;
        currentJoyVector = Vector2.zero; 
    }

    // ==========================================
    // VÒNG LẶP XỬ LÝ CHÍNH
    // ==========================================
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.L))
        {
            aimbotActive = !aimbotActive;
            if (!aimbotActive)
            {
                lockedEnemyHead = null;
                headLocked = false;
            }
        }

        // Gọi hàm Aim mỗi frame nếu có Camera
        if (cameraTransform != null)
        {
            Aim(currentJoyVector, isFiring);
        }
    }

    // ==========================================
    // LOGIC NGẮM TRỢ LỰC VÀ AIMBOT
    // ==========================================
    private void Aim(Vector2 joy, bool firing)
    {
        float mag = Mathf.Clamp01(joy.magnitude);
        float curve = (responseCurve != null && responseCurve.keys.Length > 0) ? responseCurve.Evaluate(mag) : mag;
        float sens = Mathf.Min(baseSensitivity + curve * dynamicBoost, maxSensitivity);

        float deltaX = joy.x * sens * Time.unscaledDeltaTime * horizontalBias;
        float deltaY = joy.y * sens * Time.unscaledDeltaTime;

        if (aimbotActive)
        {
            HandleAimbotMode(ref deltaX);
        }
        else
        {
            HandleAssistMode(joy, ref deltaX, deltaY, firing);
        }

        currentPitch = Mathf.Clamp(currentPitch, minPitch, maxPitch);
        
        // CẬP NHẬT TRỰC TIẾP LÊN CAMERA
        cameraTransform.localRotation = Quaternion.Euler(currentPitch, 0f, 0f);
        ApplyYaw(deltaX);
        
        pitch = currentPitch;
    }

    private void HandleAssistMode(Vector2 joy, ref float deltaX, float deltaY, bool firing)
    {
        float freePitch = currentPitch - deltaY;

        if (!firing)
        {
            currentPitch = freePitch;
            headLocked = false;
            return;
        }

        if (currentTargetHead == null || !IsTargetVisible(currentTargetHead))
        {
            currentTargetHead = FindNearestHeadInView();
        }

        if (currentTargetHead != null)
        {
            Vector3 headPos = currentTargetHead.position;
            Vector3 bodyPos = headPos - new Vector3(0, bodyOffset, 0);

            float pitchToHead = CalculatePitchTo(headPos);
            float pitchToBody = CalculatePitchTo(bodyPos);

            if (!headLocked)
            {
                if (joy.y >= snapUpTrigger)
                {
                    currentPitch = Mathf.LerpAngle(currentPitch, pitchToHead, aimSmoothSpeed * Time.unscaledDeltaTime);
                    ApplySoftMagnet(joy.x, ref deltaX, headPos);
                    
                    if (Mathf.Abs(currentPitch - pitchToHead) < 2f) 
                        headLocked = true;
                }
                else if (joy.magnitude < 0.3f) 
                {
                    currentPitch = Mathf.LerpAngle(currentPitch, pitchToBody, aimSmoothSpeed * 0.7f * Time.unscaledDeltaTime);
                    ApplySoftMagnet(joy.x, ref deltaX, bodyPos);
                }
                else
                {
                    currentPitch = freePitch; 
                }
            }
            else 
            {
                currentPitch = Mathf.LerpAngle(currentPitch, pitchToHead, aimSmoothSpeed * Time.unscaledDeltaTime);
                ApplySoftMagnet(joy.x, ref deltaX, headPos);

                if (joy.y <= unlockDownTrigger)
                {
                    headLocked = false;
                    currentPitch = freePitch; 
                }
            }
        }
        else
        {
            currentPitch = freePitch;
        }
    }

    private void HandleAimbotMode(ref float deltaX)
    {
        if (lockedEnemyHead == null || !IsTargetVisible(lockedEnemyHead))
            lockedEnemyHead = FindBestEnemyHead();

        if (lockedEnemyHead != null)
        {
            headLocked = true;
            Vector3 dirToHead = (lockedEnemyHead.position - cameraTransform.position).normalized;
            
            float targetPitch = -Mathf.Asin(dirToHead.y) * Mathf.Rad2Deg;
            float targetYaw = Mathf.Atan2(dirToHead.x, dirToHead.z) * Mathf.Rad2Deg;

            currentPitch = Mathf.LerpAngle(currentPitch, targetPitch, aimbotSnapSpeed * Time.unscaledDeltaTime);
            
            float currentYaw = GetCurrentYaw();
            float newYaw = Mathf.LerpAngle(currentYaw, targetYaw, aimbotSnapSpeed * Time.unscaledDeltaTime);
            deltaX = Mathf.DeltaAngle(currentYaw, newYaw);

            Debug.DrawLine(cameraTransform.position, lockedEnemyHead.position, Color.red);
        }
    }

    // ==========================================
    // HELPERS & TÍNH TOÁN
    // ==========================================
    private void ApplySoftMagnet(float joyX, ref float deltaX, Vector3 targetPosition)
    {
        if (Mathf.Abs(joyX) < 0.5f) 
        {
            Vector3 localTarget = cameraTransform.InverseTransformPoint(targetPosition);
            float angleToTarget = Mathf.Atan2(localTarget.x, localTarget.z) * Mathf.Rad2Deg;

            if (Mathf.Abs(angleToTarget) < yawMagnetFOV * 0.5f)
            {
                float correction = Mathf.Lerp(0, angleToTarget, yawMagnetStrength * Time.unscaledDeltaTime);
                correction = Mathf.Clamp(correction, -maxYawCorrectionSpeed * Time.unscaledDeltaTime, maxYawCorrectionSpeed * Time.unscaledDeltaTime);
                deltaX += correction;
            }
        }
    }

    private float CalculatePitchTo(Vector3 targetPos)
    {
        Vector3 dir = (targetPos - cameraTransform.position).normalized;
        return -Mathf.Asin(dir.y) * Mathf.Rad2Deg;
    }

    private float GetCurrentYaw()
    {
        return cameraTransform.parent != null ? cameraTransform.parent.eulerAngles.y : cameraTransform.eulerAngles.y;
    }

    private void ApplyYaw(float deltaYaw)
    {
        if (cameraTransform.parent != null)
            cameraTransform.parent.Rotate(Vector3.up * deltaYaw);
        else
            cameraTransform.Rotate(Vector3.up * deltaYaw, Space.World);
    }

    private Transform FindNearestHeadInView()
    {
        Collider[] hits = Physics.OverlapSphere(cameraTransform.position, 60f, enemyLayer);
        Transform best = null;
        float bestDot = 0.4f; 

        foreach (var col in hits)
        {
            Transform head = col.transform.Find("Head") ?? col.transform;
            Vector3 dir = (head.position - cameraTransform.position).normalized;
            float dot = Vector3.Dot(cameraTransform.forward, dir);

            if (dot > bestDot && dot > Mathf.Cos(yawMagnetFOV * 0.5f * Mathf.Deg2Rad))
            {
                bestDot = dot;
                best = head;
            }
        }
        return best;
    }

    private Transform FindBestEnemyHead()
    {
        Collider[] hits = Physics.OverlapSphere(cameraTransform.position, aimbotRange, enemyLayer);
        Transform best = null;
        float bestScore = 0f;

        foreach (var col in hits)
        {
            Transform head = col.transform.Find("Head") ?? col.transform;
            Vector3 dir = (head.position - cameraTransform.position).normalized;
            float dist = Vector3.Distance(cameraTransform.position, head.position);
            float dot = Vector3.Dot(cameraTransform.forward, dir);

            if (dot > Mathf.Cos(aimbotFOV * 0.5f * Mathf.Deg2Rad))
            {
                float score = dot * (1f / (dist + 1f)); 
                if (score > bestScore && IsTargetVisible(head))
                {
                    bestScore = score;
                    best = head;
                }
            }
        }
        return best;
    }

    private bool IsTargetVisible(Transform target)
    {
        if (target == null) return false;
        Vector3 dir = target.position - cameraTransform.position;
        return !Physics.Raycast(cameraTransform.position, dir.normalized, dir.magnitude, ~enemyLayer); 
    }
}
