using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 基础 ARPG 操控：
///  - WASD 移动（相对相机方向）
///  - 角色朝向鼠标所指的地面位置
///  - 左键攻击、空格翻滚、左 Shift 冲刺
/// 使用新 Input System 直接读取键鼠，无需配置 Input Actions 资源。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerControll : MonoBehaviour
{
    [Header("移动")]
    public float moveSpeed = 5f;
    public float sprintSpeed = 8f;
    public float gravity = -20f;

    [Header("朝向")]
    [Tooltip("转向速度（度/秒）")]
    public float turnSpeed = 1080f;
    [Tooltip("鼠标射线检测的地面层；为 Nothing 时使用角色脚下的水平面")]
    public LayerMask groundMask;

    [Header("翻滚")]
    public float dodgeSpeed = 12f;
    public float dodgeDuration = 0.25f;
    public float dodgeCooldown = 0.6f;

    [Header("攻击")]
    public float attackCooldown = 0.4f;
    [Tooltip("攻击时是否锁定移动")]
    public bool lockMoveWhileAttacking = true;
    public float attackLockTime = 0.3f;

    [Header("引用（可选）")]
    public Camera cam;
    public Animator animator;

    CharacterController controller;
    float verticalVelocity;

    Vector3 dodgeDir;
    float dodgeTimer;
    float dodgeCooldownTimer;

    float attackCooldownTimer;
    float attackLockTimer;

    static readonly int SpeedHash = Animator.StringToHash("Speed");
    static readonly int AttackHash = Animator.StringToHash("Attack");
    static readonly int DodgeHash = Animator.StringToHash("Dodge");

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (cam == null) cam = Camera.main;
        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        if (keyboard == null || mouse == null) return;

        float dt = Time.deltaTime;
        dodgeCooldownTimer -= dt;
        attackCooldownTimer -= dt;
        attackLockTimer -= dt;

        // ---------- 读取 WASD ----------
        Vector2 input = Vector2.zero;
        if (keyboard.wKey.isPressed) input.y += 1;
        if (keyboard.sKey.isPressed) input.y -= 1;
        if (keyboard.dKey.isPressed) input.x += 1;
        if (keyboard.aKey.isPressed) input.x -= 1;
        input = Vector2.ClampMagnitude(input, 1f);

        // 相对相机的水平方向
        Vector3 moveDir = CameraRelative(input);

        // ---------- 翻滚 ----------
        if (keyboard.spaceKey.wasPressedThisFrame && dodgeCooldownTimer <= 0f && dodgeTimer <= 0f)
        {
            dodgeDir = moveDir.sqrMagnitude > 0.01f ? moveDir.normalized : transform.forward;
            dodgeTimer = dodgeDuration;
            dodgeCooldownTimer = dodgeCooldown;
            transform.rotation = Quaternion.LookRotation(dodgeDir);
            if (animator) animator.SetTrigger(DodgeHash);
        }

        // ---------- 攻击 ----------
        bool attacking = attackLockTimer > 0f;
        if (mouse.leftButton.wasPressedThisFrame && attackCooldownTimer <= 0f && dodgeTimer <= 0f)
        {
            FaceMouse(mouse, instant: true);
            attackCooldownTimer = attackCooldown;
            attackLockTimer = attackLockTime;
            attacking = true;
            if (animator) animator.SetTrigger(AttackHash);
            OnAttack();
        }

        // ---------- 计算水平速度 ----------
        Vector3 horizontal;
        if (dodgeTimer > 0f)
        {
            dodgeTimer -= dt;
            horizontal = dodgeDir * dodgeSpeed;
        }
        else
        {
            float speed = keyboard.leftShiftKey.isPressed ? sprintSpeed : moveSpeed;
            horizontal = (attacking && lockMoveWhileAttacking) ? Vector3.zero : moveDir * speed;
            FaceMouse(mouse, instant: false);
        }

        // ---------- 重力 ----------
        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f; // 保持贴地
        verticalVelocity += gravity * dt;

        controller.Move((horizontal + Vector3.up * verticalVelocity) * dt);

        if (animator) animator.SetFloat(SpeedHash, horizontal.magnitude);
    }

    Vector3 CameraRelative(Vector2 input)
    {
        if (cam == null) return new Vector3(input.x, 0f, input.y);

        Vector3 forward = cam.transform.forward;
        Vector3 right = cam.transform.right;
        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();
        return forward * input.y + right * input.x;
    }

    /// <summary>让角色朝向鼠标在地面上的位置。</summary>
    void FaceMouse(Mouse mouse, bool instant)
    {
        if (!TryGetMouseWorldPoint(mouse, out Vector3 point)) return;

        Vector3 dir = point - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;

        Quaternion target = Quaternion.LookRotation(dir);
        transform.rotation = instant
            ? target
            : Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
    }

    bool TryGetMouseWorldPoint(Mouse mouse, out Vector3 point)
    {
        point = default;
        if (cam == null) return false;

        Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());

        if (groundMask.value != 0 &&
            Physics.Raycast(ray, out RaycastHit hit, 500f, groundMask, QueryTriggerInteraction.Ignore))
        {
            point = hit.point;
            return true;
        }

        // 未设置地面层或没打中时，与角色所在高度的水平面求交
        Plane plane = new Plane(Vector3.up, transform.position);
        if (plane.Raycast(ray, out float enter))
        {
            point = ray.GetPoint(enter);
            return true;
        }
        return false;
    }

    /// <summary>攻击逻辑入口：在这里做伤害判定、特效等。</summary>
    protected virtual void OnAttack()
    {
        Debug.Log("Attack!");
    }
}
