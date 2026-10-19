using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace StarterAssets
{
	/// <summary>
	/// StarterAssets first-person controller, reworked for Mazraa (Farming Simulator 25 style on foot, see MoveFeel):
	/// walk ~1.5 m/s, run ~5 m/s, quick linear acceleration / braking with fast turns (no ice-skating), ground snapping
	/// on slopes and steps (no bouncing downhill), coyote time and a jump buffer, smooth crouch, slight head bob,
	/// raw mouse look and a curved gamepad look. Speed, sprint, crouch and acceleration come from PlayerLife.
	/// Everything runs in Update / LateUpdate with rate x dt, so it feels the same at 30, 60 or 144 fps.
	/// </summary>
	[RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM
	[RequireComponent(typeof(PlayerInput))]
#endif
	public class FirstPersonController : MonoBehaviour
	{
		[Header("Player")]
		[Tooltip("Walk speed of the character in m/s (set from MoveFeel at start)")]
		public float MoveSpeed = MoveFeel.WalkSpeed;
		[Tooltip("Run speed of the character in m/s (set from MoveFeel at start)")]
		public float SprintSpeed = MoveFeel.RunSpeed;
		[Tooltip("Look speed multiplier")]
		public float RotationSpeed = 1.0f;
		[Tooltip("Unused (kept for the scene's serialized value); acceleration comes from MoveFeel")]
		public float SpeedChangeRate = 10.0f;

		[Space(10)]
		[Tooltip("The height the player can jump (set from MoveFeel at start)")]
		public float JumpHeight = MoveFeel.JumpHeight;
		[Tooltip("The character uses its own gravity value (set from MoveFeel at start)")]
		public float Gravity = MoveFeel.Gravity;

		[Space(10)]
		[Tooltip("Time required to pass before being able to jump again")]
		public float JumpTimeout = MoveFeel.JumpCooldown;
		[Tooltip("Unused (kept for the scene's serialized value)")]
		public float FallTimeout = 0.15f;

		[Header("Player Grounded")]
		[Tooltip("If the character is grounded or not. Not part of the CharacterController built in grounded check")]
		public bool Grounded = true;
		[Tooltip("Unused (the ground check is sized from the CharacterController)")]
		public float GroundedOffset = -0.14f;
		[Tooltip("Unused (the ground check is sized from the CharacterController)")]
		public float GroundedRadius = 0.5f;
		[Tooltip("Extra layers the character uses as ground (everything but Player / Interactable / Outline / Ignore Raycast already counts)")]
		public LayerMask GroundLayers;

		[Header("Cinemachine")]
		[Tooltip("The follow target set in the Cinemachine Virtual Camera that the camera will follow")]
		public GameObject CinemachineCameraTarget;
		[Tooltip("How far in degrees can you move the camera up")]
		public float TopClamp = 89.0f;
		[Tooltip("How far in degrees can you move the camera down")]
		public float BottomClamp = -89.0f;

		/// <summary>Current flat velocity (m/s).</summary>
		public Vector3 PlanarVelocity => _velocity;
		public float GroundSpeed => _velocity.magnitude;

		// cinemachine
		private float _cinemachineTargetPitch;

		// player
		private Vector3 _velocity;
		private float _verticalVelocity;
		private float _coyote, _jumpBuffer, _jumpCooldown;
		private bool _jumpHeld;
		private float _lookHold;

#if ENABLE_INPUT_SYSTEM
		private PlayerInput _playerInput;
#endif
		private CharacterController _controller;
		private StarterAssetsInputs _input;
		private GameObject _mainCamera;

		// Mazraa: crouch (PlayerLife.Crouching) lowers the capsule and the camera smoothly; head bob on the camera root.
		private float _standHeight, _standCenterY, _standCameraY, _standCameraX, _crouchBlend;
		private bool _crouchInit;
		public float CrouchDrop = 0.7f;
		private float _bobPhase;
		private Vector2 _bob;

		private bool IsCurrentDeviceMouse
		{
			get
			{
#if ENABLE_INPUT_SYSTEM
				return _playerInput == null || _playerInput.currentControlScheme == "KeyboardMouse";
#else
				return true;
#endif
			}
		}

		private int GroundMask => GroundLayers.value | Build.GroundMask;

		private void Awake()
		{
			// get a reference to our main camera
			if (_mainCamera == null)
			{
				_mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
			}
		}

		private void Start()
		{
			_controller = GetComponent<CharacterController>();
			_input = GetComponent<StarterAssetsInputs>();
#if ENABLE_INPUT_SYSTEM
			_playerInput = GetComponent<PlayerInput>();
#else
			Debug.LogError( "Starter Assets package is missing dependencies. Please use Tools/Starter Assets/Reinstall Dependencies to fix it");
#endif
			// The scene still carries the old StarterAssets numbers (4 / 6 m/s, 1.2 m jump): use the Mazraa feel.
			MoveSpeed = MoveFeel.WalkSpeed;
			SprintSpeed = MoveFeel.RunSpeed;
			JumpHeight = MoveFeel.JumpHeight;
			Gravity = MoveFeel.Gravity;
			JumpTimeout = MoveFeel.JumpCooldown;
			if (TopClamp > 89f) TopClamp = 89f;
			if (BottomClamp < -89f) BottomClamp = -89f;

			// Capsule: a bigger skin and step so small edges and kerbs don't catch, slopes up to 50 degrees.
			_controller.skinWidth = Mathf.Max(MoveFeel.SkinWidth, _controller.radius * 0.1f);
			_controller.stepOffset = Mathf.Min(MoveFeel.StepOffset, _controller.height * 0.5f);
			_controller.slopeLimit = MoveFeel.SlopeLimit;
			_controller.minMoveDistance = 0f;

			if (CinemachineCameraTarget != null)
			{
				float pitch = CinemachineCameraTarget.transform.localEulerAngles.x;
				_cinemachineTargetPitch = pitch > 180f ? pitch - 360f : pitch;
			}
		}

		private void Update()
		{
			float dt = Time.deltaTime;
			if (dt <= 0f) return;   // paused
			GroundedCheck();
			float rise = JumpAndGravity(dt, out bool jumped);
			Move(dt, jumped, rise);
			Crouch(dt);
		}

		private void LateUpdate()
		{
			CameraRotation();
		}

		// ------------------------------------------------------------------ ground

		/// <summary>Centre of the capsule's bottom sphere in world space.</summary>
		private Vector3 BottomSphere()
		{
			Vector3 c = transform.position + _controller.center;
			return c + Vector3.down * Mathf.Max(0f, _controller.height * 0.5f - _controller.radius);
		}

		private void GroundedCheck()
		{
			// a slightly smaller sphere a few cm below the capsule: finds the ground, never a wall beside you
			float r = _controller.radius * 0.9f;
			Vector3 probe = BottomSphere() + Vector3.down * (_controller.skinWidth + 0.06f + _controller.radius * 0.1f);
			Grounded = _controller.isGrounded || Physics.CheckSphere(probe, r, GroundMask, QueryTriggerInteraction.Ignore);
		}

		/// <summary>Vertical speed for this frame; returns the vertical move (m), integrated exactly (same jump height at any fps).</summary>
		private float JumpAndGravity(float dt, out bool jumped)
		{
			jumped = false;
			// press edge (StarterAssets keeps jump true while held); PlayerLife may clear it when out of stamina
			bool pressed = _input.jump && !_jumpHeld;
			_jumpHeld = _input.jump;
			_jumpBuffer = pressed ? MoveFeel.JumpBuffer : _jumpBuffer - dt;
			_jumpCooldown -= dt;
			_coyote = Grounded && _verticalVelocity <= 0.01f ? MoveFeel.CoyoteTime : _coyote - dt;

			if (_jumpBuffer > 0f && _coyote > 0f && _jumpCooldown <= 0f && PlayerLife.JumpAllowed)
			{
				_verticalVelocity = MoveFeel.JumpVelocity(JumpHeight, Gravity);
				_jumpBuffer = 0f; _coyote = 0f; _jumpCooldown = JumpTimeout;
				jumped = true;
			}
			else if (Grounded && _verticalVelocity <= 0f)
			{
				_verticalVelocity = MoveFeel.GroundStick;
				return _verticalVelocity * dt;
			}
			return MoveFeel.Fall(ref _verticalVelocity, dt, Gravity);
		}

		// ------------------------------------------------------------------ move

		private void Move(float dt, bool jumped, float rise)
		{
			// Mazraa: stamina, carried weight and crouching (PlayerLife) limit sprinting and scale the speed.
			Vector2 move = _input.move;
			float tilt = Mathf.Sqrt(move.x * move.x + move.y * move.y);
			if (IsCurrentDeviceMouse && !_input.analogMovement && tilt > 0.001f) tilt = 1f;   // keys: always full speed
			float targetSpeed = MoveFeel.TargetSpeed(_input.sprint, PlayerLife.SprintAllowed, PlayerLife.SpeedFactor, tilt, MoveSpeed, SprintSpeed);

			Vector3 wish = Vector3.zero;
			if (tilt > 0.001f)
			{
				Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
				Vector3 right = transform.right; right.y = 0f; right.Normalize();
				wish = (right * move.x + fwd * move.y).normalized * targetSpeed;
			}
			bool grounded = Grounded && !jumped;
			Vector3 old = _velocity;
			_velocity = MoveFeel.StepVelocity(_velocity, wish, dt, PlayerLife.AccelerationFactor, grounded);

			Vector3 before = transform.position;
			Vector3 step = MoveFeel.StepMove(old, _velocity, dt);   // average speed over the frame: same distance at any fps
			CollisionFlags flags = _controller.Move(step + Vector3.up * rise);

			// Walking down a slope or off a kerb: stay on the ground instead of flying off and bouncing.
			if (grounded && _verticalVelocity <= 0f && (flags & CollisionFlags.Below) == 0)
			{
				float snap = MoveFeel.SnapDistance(step.magnitude, _controller.stepOffset, _controller.slopeLimit);
				float r = _controller.radius * 0.9f;
				if (Physics.SphereCast(BottomSphere(), r, Vector3.down, out RaycastHit hit, snap + _controller.radius * 0.1f + _controller.skinWidth, GroundMask, QueryTriggerInteraction.Ignore)
					&& Vector3.Angle(hit.normal, Vector3.up) <= _controller.slopeLimit + 1f)
				{
					flags |= _controller.Move(Vector3.down * hit.distance);
					Grounded = true;
				}
			}

			// A wall took part of the step: keep only what really happened (slide along it, no stored push).
			// (only a real block: a slope also reports Sides but keeps nearly all of the horizontal step)
			if ((flags & CollisionFlags.Sides) != 0)
			{
				Vector3 actual = (transform.position - before) / dt;
				float actualFlat = new Vector3(actual.x, 0f, actual.z).magnitude, wantedFlat = new Vector3(step.x, 0f, step.z).magnitude / dt;
				if (actualFlat < wantedFlat * 0.9f) _velocity = MoveFeel.AfterCollision(_velocity, actual);
			}
			// Bumped the head: start falling at once.
			if ((flags & CollisionFlags.Above) != 0 && _verticalVelocity > 0f) _verticalVelocity = 0f;
		}

		// ------------------------------------------------------------------ crouch + head bob (camera root height)

		private void Crouch(float dt)
		{
			if (!_crouchInit)
			{
				_crouchInit = true;
				_standHeight = _controller.height;
				_standCenterY = _controller.center.y;
				_standCameraY = CinemachineCameraTarget != null ? CinemachineCameraTarget.transform.localPosition.y : 0f;
				_standCameraX = CinemachineCameraTarget != null ? CinemachineCameraTarget.transform.localPosition.x : 0f;
			}
			float target = PlayerLife.Crouching ? 1f : 0f;
			if (!Mathf.Approximately(target, _crouchBlend))
			{
				_crouchBlend = Mathf.MoveTowards(_crouchBlend, target, dt / MoveFeel.CrouchTime);
				float d = CrouchDrop * Mathf.SmoothStep(0f, 1f, _crouchBlend);
				_controller.height = _standHeight - d;
				_controller.center = new Vector3(_controller.center.x, _standCenterY - d * 0.5f, _controller.center.z);
			}

			// slight head bob from the distance walked (Settings > Controls > Head bob, off with Reduce motion)
			float speed = _velocity.magnitude;
			Vector2 bobTarget = Vector2.zero;
			if (MoveFeel.BobEnabled && (ThirdPersonView.Instance == null || !ThirdPersonView.Instance.IsThirdPerson))
			{
				_bobPhase = MoveFeel.AdvanceBob(_bobPhase, speed, dt);
				bobTarget = MoveFeel.BobOffset(_bobPhase, speed, Grounded);
			}
			float k = MoveFeel.Ease(14f, dt);
			_bob = new Vector2(Mathf.Lerp(_bob.x, bobTarget.x, k), Mathf.Lerp(_bob.y, bobTarget.y, k));

			if (CinemachineCameraTarget != null)
			{
				float drop = CrouchDrop * Mathf.SmoothStep(0f, 1f, _crouchBlend);
				var lp = CinemachineCameraTarget.transform.localPosition;
				CinemachineCameraTarget.transform.localPosition = new Vector3(_standCameraX + _bob.x, _standCameraY - drop + _bob.y, lp.z);
			}
		}

		// ------------------------------------------------------------------ look

		private void CameraRotation()
		{
			if (_input == null || CinemachineCameraTarget == null) return;
			Vector2 look = _input.look;
			float yaw, pitch;
			if (IsCurrentDeviceMouse)
			{
				// Mouse: raw per-frame delta (already scaled by the input actions and the sensitivity), no smoothing,
				// no threshold (the old 0.01 threshold dropped slow movements, worst at high frame rates).
				if (look.x == 0f && look.y == 0f) return;
				yaw = look.x * RotationSpeed;
				pitch = look.y * RotationSpeed;
			}
			else
			{
				// Gamepad: stick -> response curve + turn boost (MoveFeel), times Settings > Controls > Stick sensitivity.
				float dt = Time.deltaTime;
				Vector2 rate = MoveFeel.GamepadLookRate(look.x / MoveFeel.StickLookScale, look.y / MoveFeel.StickLookScale, dt, ref _lookHold);
				float sens = GameSettings.StickSensitivity * RotationSpeed;
				yaw = rate.x * sens * dt;
				pitch = rate.y * sens * dt;
				if (yaw == 0f && pitch == 0f) return;
			}

			_cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch + pitch, BottomClamp, TopClamp);
			CinemachineCameraTarget.transform.localRotation = Quaternion.Euler(_cinemachineTargetPitch, 0.0f, 0.0f);
			transform.Rotate(Vector3.up * yaw);
		}

		private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
		{
			if (lfAngle < -360f) lfAngle += 360f;
			if (lfAngle > 360f) lfAngle -= 360f;
			return Mathf.Clamp(lfAngle, lfMin, lfMax);
		}

		private void OnDrawGizmosSelected()
		{
			var cc = GetComponent<CharacterController>();
			if (cc == null) return;
			Gizmos.color = Grounded ? new Color(0.0f, 1.0f, 0.0f, 0.35f) : new Color(1.0f, 0.0f, 0.0f, 0.35f);
			Vector3 bottom = transform.position + cc.center + Vector3.down * Mathf.Max(0f, cc.height * 0.5f - cc.radius);
			Gizmos.DrawSphere(bottom + Vector3.down * (cc.skinWidth + 0.06f + cc.radius * 0.1f), cc.radius * 0.9f);
		}
	}
}
