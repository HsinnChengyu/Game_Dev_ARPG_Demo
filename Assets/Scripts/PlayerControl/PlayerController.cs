using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 绝区零风格 ARPG 操控（Root Motion 驱动位移）：
///  - 输入来自 Project-wide Actions（InputSystem_Actions 的 Player 表）
///  - 代码只负责写 Animator 参数、切换状态和转向，位移由动画的根运动提供
///  - Move 移动（相对相机）、Sprint 跑步、Attack 连击、Dodge 闪避
/// 需与 Animator 挂在同一个物体上（OnAnimatorMove 只会在该物体上回调）。
/// 对应 Animator 参数：Movement(float) / HasInput(bool) / Run(bool) / TurnBack(bool) / Lock(float)
/// </summary>
[RequireComponent(typeof(CharacterController), typeof(Animator))]
public class PlayerController : MonoBehaviour
{
    [Header("移动")]
    [Tooltip("走路时写入 Movement 的值（Normal 混合树 0.5~2 为走）")]
    public float walkValue = 1f;
    [Tooltip("跑步时写入 Movement 的值（Normal 混合树 3 为跑）")]
    public float runValue = 3f;
    [Tooltip("勾选后默认跑步，否则按住 Sprint 才跑")]
    public bool runByDefault = false;
    public float movementDampTime = 0.1f;
    public float gravity = -20f;

    [Header("转向")]
    [Tooltip("转向速度（度/秒）")]
    public float turnSpeed = 720f;
    [Tooltip("跑步中输入方向与朝向夹角超过该值时触发转身跑")]
    public float turnBackAngle = 150f;

    [Header("攻击（状态名需与 Animator 中一致）")]
    public string[] attackStates =
        { "Anbi_Normal_1", "Anbi_Normal_2", "Anbi_Normal_3", "Anbi_Normal_4", "Anbi_Normal_5" };
    [Tooltip("当前攻击播放到该归一化进度后才接受下一段连击")]
    [Range(0f, 1f)] public float comboInputStart = 0.4f;
    [Tooltip("离开攻击状态超过该时间则连击重置")]
    public float comboResetTime = 0.6f;
    [Tooltip("提前按下攻击的缓存时间")]
    public float inputBufferTime = 0.3f;

    [Header("闪避")]
    public string dodgeFrontState = "Dodge_Front";
    public string dodgeBackState = "Dodge_Back";
    public float dodgeCooldown = 0.3f;

    [Header("通用")]
    public float crossFadeTime = 0.1f;
    public Camera cam;

    CharacterController controller;
    Animator animator;

    InputAction moveAction;
    InputAction sprintAction;
    InputAction attackAction;
    InputAction dodgeAction;

    float verticalVelocity;
    float dodgeCooldownTimer;
    float attackBufferTimer;
    float lastAttackActiveTime = float.NegativeInfinity;
    int comboIndex = -1;

    static readonly int MovementHash = Animator.StringToHash("Movement");
    static readonly int HasInputHash = Animator.StringToHash("HasInput");
    static readonly int RunHash = Animator.StringToHash("Run");
    static readonly int TurnBackHash = Animator.StringToHash("TurnBack");

    const string TagAttack = "ATK";
    const string TagSkill = "Skill";
    const string TagHit = "Hit";
    const string TagMovement = "Movement";
    const string TagIdle = "Idle";
    const string TagTurnRun = "TurnRun";

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
        animator.applyRootMotion = true;
        if (cam == null) cam = Camera.main;

        moveAction = InputSystem.actions.FindAction("Player/Move", throwIfNotFound: true);
        sprintAction = InputSystem.actions.FindAction("Player/Sprint", throwIfNotFound: true);
        attackAction = InputSystem.actions.FindAction("Player/Attack", throwIfNotFound: true);
        dodgeAction = InputSystem.actions.FindAction("Player/Dodge", throwIfNotFound: true);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        dodgeCooldownTimer -= dt;
        attackBufferTimer -= dt;

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (state.IsTag(TagAttack)) lastAttackActiveTime = Time.time;

        // ---------- 读取输入 ----------
        Vector2 input = moveAction.ReadValue<Vector2>();
        bool hasInput = input.sqrMagnitude > 0.01f;
        Vector3 moveDir = CameraRelative(input);

        // ---------- 移动参数 ----------
        bool run = hasInput && (runByDefault || sprintAction.IsPressed());
        animator.SetBool(HasInputHash, hasInput);
        animator.SetBool(RunHash, run);
        // 松开时保留 Movement，用于判断进入 Walk_End 还是 Run_End
        if (hasInput)
            animator.SetFloat(MovementHash, run ? runValue : walkValue, movementDampTime, dt);

        // ---------- 转身跑 ----------
        bool turnBack = hasInput && state.IsTag(TagMovement)
                        && animator.GetFloat(MovementHash) > 2.5f
                        && Vector3.Angle(transform.forward, moveDir) > turnBackAngle;
        animator.SetBool(TurnBackHash, turnBack);

        // ---------- 闪避（可取消攻击） ----------
        if (dodgeAction.WasPressedThisFrame() && dodgeCooldownTimer <= 0f && !state.IsTag(TagHit))
        {
            if (hasInput)
            {
                transform.rotation = Quaternion.LookRotation(moveDir);
                PlayState(dodgeFrontState);
            }
            else
            {
                PlayState(dodgeBackState);
            }
            dodgeCooldownTimer = dodgeCooldown;
            attackBufferTimer = 0f;
            comboIndex = -1;
        }
        else
        {
            HandleAttack(state, hasInput, moveDir);
        }

        // ---------- 自由移动时转向输入方向 ----------
        bool canTurn = hasInput && !turnBack
                       && (state.IsTag(TagMovement) || state.IsTag(TagIdle))
                       && !animator.GetNextAnimatorStateInfo(0).IsTag(TagTurnRun);
        if (canTurn)
        {
            Quaternion target = Quaternion.LookRotation(moveDir);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * dt);
        }

        // ---------- 重力 ----------
        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f; // 保持贴地
        verticalVelocity += gravity * dt;
    }

    void HandleAttack(AnimatorStateInfo state, bool hasInput, Vector3 moveDir)
    {
        if (attackAction.WasPressedThisFrame()) attackBufferTimer = inputBufferTime;
        if (attackBufferTimer <= 0f || attackStates.Length == 0) return;

        // 受击、放技能、过渡中不接受攻击；攻击前段先缓存输入
        if (state.IsTag(TagHit) || state.IsTag(TagSkill) || animator.IsInTransition(0)) return;
        if (state.IsTag(TagAttack) && state.normalizedTime < comboInputStart) return;

        bool continueCombo = Time.time - lastAttackActiveTime < comboResetTime;
        comboIndex = continueCombo ? (comboIndex + 1) % attackStates.Length : 0;

        if (hasInput) transform.rotation = Quaternion.LookRotation(moveDir);
        PlayState(attackStates[comboIndex]);
        attackBufferTimer = 0f;
        lastAttackActiveTime = Time.time;
        OnAttack(comboIndex);
    }

    /// <summary>由动画根运动驱动位移与旋转，叠加代码计算的重力。</summary>
    void OnAnimatorMove()
    {
        Vector3 delta = animator.deltaPosition;
        delta.y = verticalVelocity * Time.deltaTime;
        controller.Move(delta);
        transform.rotation *= animator.deltaRotation;
    }

    void PlayState(string stateName)
    {
        animator.CrossFadeInFixedTime(stateName, crossFadeTime, 0);
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

    /// <summary>攻击逻辑入口：在这里做伤害判定、特效等。</summary>
    protected virtual void OnAttack(int combo)
    {
        Debug.Log($"Attack {combo + 1}");
    }
}
