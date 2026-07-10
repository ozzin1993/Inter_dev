using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem.Utilities;
using StrategyCore;

namespace Camera_TopDownNS
{
    // Top-Down or 2D
    public enum CameraType
    {
        TopDown_3D,
        _2D
    }

    // Boundary Square or Round
    public enum BoundaryType
    {
        Square,
        Circle
    }

    // Camera Class
    public class Camera_TopDown : MonoBehaviour
    {
        public static Camera_TopDown instance;

        [SerializeField] private Transform cameraTransform;
        [SerializeField] private CameraType cameraType;
        [SerializeField] private RectTransform virtualCursorTransform;
        public Transform followTransform;

        [Header("General sensitivity")]
        [Tooltip("SlowMode activated by Shift on keyboard and Left Trigger on gamepad - slows down zoom, rotation and move")]
        [SerializeField] private float slowSpeed = 0.3f;

        [Space(10)]
        [Tooltip("When cursor is at the Screen Edge")]
        public float edgeMoveSensitivity = 1f;
        [Tooltip("Vector move - WASD, Arrows, LeftStick")]
        [SerializeField] private float moveSensitivitiy = 60f;
        private float movementSpeed;

        [Space(10)]
        [Tooltip("Button rotation - E,Q on Keyboard, DPAD on gamepad")]
        [SerializeField] private float buttonRotationSensitivitiy = 170f;
        [Tooltip("Button zoom - R,F on Keyboard, Shoulders on gamepad")]
        [SerializeField] private float buttonZoomSensitivitiy = 250;

        [Header("Gamepad Sensitivity")]
        [Tooltip("Sensitivity of virtual mouse controlled by RightStick on Gamepad")]
        [SerializeField] private float virtualMouseSensitivity = 800f;

        [Header("Mouse Sensitivity")]
        [SerializeField] private float mouseZoomSensitivity = 25f;
        [SerializeField] private float mouseRotationSensitivity = 0.2f;

        [Header("Touch Sensitivity")]
        [SerializeField] private float touchZoomSensitivity = 10f;
        [SerializeField] private float touchRotationSensitivity = 0.2f;

        [Header("Zoom Limits")]
        [SerializeField] private int zoomMin = 10;
        [SerializeField] private int zoomMax = 100;

        [Header("Position Limits")]
        [SerializeField] private BoundaryType boundaryType;
        [Tooltip("___X\nW___Y\n___Z")]
        public Vector4 squareLimits = new Vector4(10, 10, 10, 10);
        [SerializeField] private int circleLimits = 100;

        [Header("Rotation/Tilt Limit")]
        [SerializeField] private Vector2 rotationMinMax;
        [SerializeField] private Vector2 tiltMinMax;

        [Header("Technical")]
        [SerializeField] private float lerpTime = 10;
        [SerializeField] private float floorLevel = 0;
        [Tooltip("For moving camera when cursor is on the edge of screen")]
        [SerializeField] private int edgePadding = 10;

        // Technical
        public static int currentInputDevice; // 0 - Mouse,Keyboard, 1 - Gamepad, 2 - Touch
        public bool cursorHidden = false; // If currently cursor hidden (Example: in placement mode)
        private bool isOverUI; // If cursor over UI

        private Mouse virtualMouse;
        public static CameraControls cameraControls;

        // Transform
        private Vector3 transformForward;
        private Vector3 transformRight;

        // 2D
        int multiplier2D = 1; // For 3D positive, for 2D negative. Used in rotation
        private Vector3 currentUpVector = new Vector3(0, 1, 0); // Vector3.up for 3D horizontal rotation

        // Events
        public delegate void OnClickAction(Vector2 clickPosition);
        public static event OnClickAction onClick;
        public static event OnClickAction onDoubleClick;
        public static event OnClickAction onLongClick;

        // Variables
        private Vector2 cursorPosition;

        private Vector3 zoomAmount;

        private Vector3 newRotation;
        private Vector3 newPosition;
        private Vector3 newZoom;
        private Vector3 resetPosition;
        private Vector3 resetRotation;
        private Vector3 resetZoom;

        // Mouse input
        private Vector3 dragStartPosition;
        private Vector3 dragCurrentPosition;
        private Vector3 rotateStartPosition;
        private Vector3 rotateCurrentPosition;

        // Touch input
        private bool fromDoubleTouchToSingle = false; // If two fingers on touch and then one is lifted
        private bool turnOffTouchMove = false; // Should be set to true when object is being dragged

        // Start is called before the first frame update
        void Start()
        {
            // If 2D change Z axis to Y
            if (cameraType == CameraType._2D)
            {
                multiplier2D = -1;
                currentUpVector = new Vector3(0, 0, 1);
            }

            // Zoom offset based on camera initial position
            zoomAmount = cameraTransform.localPosition * -0.01f;

            // Initial transform
            newPosition = resetPosition = transform.position;
            newZoom = resetZoom = cameraTransform.localPosition;
            newRotation = resetRotation = transform.rotation.eulerAngles;
            if (cameraType == CameraType._2D) // If 2D reverse euler conversion on initial value, so clamp will work properly
            {
                newRotation[0] = newRotation[0] - 360;
            }

            // Virtual mouse initial position
            virtualCursorTransform.position = new Vector2(Screen.width / 2, Screen.height / 2);

            // Change input device
            InputSystem.onAnyButtonPress.Call(
            ctrl =>
            {
                if (ctrl.device is Gamepad gamepad)
                {
                    currentInputDevice = 1;
                }
                else if (ctrl.device is Mouse mouse || ctrl.device is Keyboard Keyboard)
                {
                    if (Mouse.current != null)
                    {
                        currentInputDevice = 0;
                    }
                }
            });
        }

        private void Awake()
        {
            cameraControls = new CameraControls();

            if (instance == null) instance = this;
        }
        private void OnEnable()
        {
            cameraControls.Enable();

            // Virtual mouse
            if (virtualMouse == null)
            {
                virtualMouse = (Mouse)InputSystem.AddDevice("VirtualMouse");
            }
            else if (!virtualMouse.added)
            {
                InputSystem.AddDevice(virtualMouse);
            }

            InputState.Change(virtualMouse.position, virtualCursorTransform.anchoredPosition);
            EnhancedTouchSupport.Enable();

        }
        private void OnDisable()
        {
            cameraControls.Disable();
            InputSystem.RemoveDevice(virtualMouse);
            EnhancedTouchSupport.Disable();
        }

        // Update is called once per frame
        void Update()
        {
            // General
            HandleGeneralInput();
            // Touch
            HandleTouchInput();
            // Gamepad
            HandleGamepadInput();
            // Mouse
            HandleMouseInput();
            // Apply input
            CalculateInput();

            // Clicks
            HandleClicks();
        }

        public void SetPosition(Vector3 position)
        {
            newPosition = position;
        }

        void HandleGeneralInput()
        {
            // Slow Mode
            if (cameraControls.GeneralMap.SlowMode.IsPressed())
            {
                movementSpeed = slowSpeed;
            }
            else
            {
                movementSpeed = 1;
            }

            // Active input Gamepad Or Mouse Or Touch
            if (ETouch.Touch.activeTouches.Count == 0)
            {
                if (currentInputDevice == 0 && Mouse.current != null)
                {
                    // Mouse
                    cursorPosition = Mouse.current.position.ReadValue();
                    //Cursor.lockState = CursorLockMode.Confined;
                    Cursor.lockState = CursorLockMode.None;
                    isOverUI = IsOverUI(cursorPosition);
                }
                else if (currentInputDevice == 1 && Gamepad.current != null)
                {
                    // Gamepad
                    cursorPosition = virtualCursorTransform.position;
                    isOverUI = IsOverUI(cursorPosition);
                }
                else
                {
                    // Touch
                    currentInputDevice = 2;
                    isOverUI = false;
                }
            }
            else
            {
                // Touch
                currentInputDevice = 2;
                cursorPosition = ETouch.Touch.activeTouches[0].screenPosition;
                isOverUI = IsOverUI(cursorPosition);
            }

            // Reset Camera
            if (cameraControls.GeneralMap.ResetCamera.WasPerformedThisFrame())
            {
                newZoom = resetZoom;
                newRotation = resetRotation;
                followTransform = null;
            }

            // Rotation Buttons
            float rotationValue = cameraControls.GeneralMap.RotationButtons.ReadValue<float>();
            if (rotationValue != 0 && !isOverUI)
            {
                newRotation += (currentUpVector * -(rotationValue * (movementSpeed * buttonRotationSensitivitiy)) * multiplier2D) * Time.deltaTime; // Rotation
            }

            // Tilt Buttons
            float tiltValue = cameraControls.GeneralMap.TiltButtons.ReadValue<float>();
            if (tiltValue != 0 && !isOverUI)
            {
                newRotation += (new Vector3(1, 0, 0) * (tiltValue * (movementSpeed * buttonRotationSensitivitiy))) * Time.deltaTime; // Tilt
            }

            // Zoom Buttons
            float zoomValue = cameraControls.GeneralMap.ZoomButtons.ReadValue<float>();
            if (zoomValue != 0 && !isOverUI)
            {
                newZoom += (zoomValue * zoomAmount * (movementSpeed * buttonZoomSensitivitiy)) * Time.deltaTime;
            }

            // Move camera - WASD, Arrows, GamePad LeftStick
            Vector2 moveVector = cameraControls.GeneralMap.MoveVector.ReadValue<Vector2>();

            // Edge move
            if (currentInputDevice == 0 || currentInputDevice == 1)
            {
                if (cursorPosition.x > Screen.width - edgePadding) { moveVector.x = 1f * edgeMoveSensitivity; }
                else if (cursorPosition.x < edgePadding) { moveVector.x = -1f * edgeMoveSensitivity; }
                if (cursorPosition.y > Screen.height - edgePadding) { moveVector.y = 1f * edgeMoveSensitivity; }
                else if (cursorPosition.y < edgePadding) { moveVector.y = -1f * edgeMoveSensitivity; }
            }

            if (cameraType == CameraType._2D)
            {
                transformForward = Quaternion.AngleAxis(transform.rotation.eulerAngles.z, currentUpVector) * Vector3.up;
                transformRight = Quaternion.AngleAxis(-90, currentUpVector) * transformForward;
            }
            else
            {
                transformForward = Quaternion.AngleAxis(transform.rotation.eulerAngles.y, currentUpVector) * Vector3.forward;
                transformRight = Quaternion.AngleAxis(90, currentUpVector) * transformForward;
            }

            newPosition += ((transformForward * moveVector.y * movementSpeed * moveSensitivitiy) + (transformRight * moveVector.x * movementSpeed * moveSensitivitiy)) * Time.deltaTime;
        }

        void HandleGamepadInput()
        {
            if (Gamepad.current != null)
            {
                // Virtual Cursor - Right stick

                Vector2 rightStick = cameraControls.GeneralMap.RightStick.ReadValue<Vector2>();

                Vector2 delta = rightStick * movementSpeed * virtualMouseSensitivity * Time.deltaTime;

                Vector3 newPosition = virtualCursorTransform.position + (new Vector3(rightStick.x * movementSpeed * virtualMouseSensitivity, rightStick.y * movementSpeed * virtualMouseSensitivity, 0) * Time.deltaTime);

                newPosition.x = Mathf.Clamp(newPosition.x, 0, Screen.width);
                newPosition.y = Mathf.Clamp(newPosition.y, 0, Screen.height);

                virtualCursorTransform.position = newPosition;

                InputState.Change(virtualMouse.position, new Vector2(newPosition.x, newPosition.y));
                InputState.Change(virtualMouse.delta, delta);

                // Virtual mouse scroll
                if (isOverUI)
                {
                    InputState.Change(virtualMouse.scroll, cameraControls.GeneralMap.Scroll.ReadValue<Vector2>() * 50f);
                }

                // Virtual mouse click
                virtualMouse.CopyState<MouseState>(out var mouseState);
                mouseState.WithButton(MouseButton.Left, cameraControls.GeneralMap.Click.IsPressed());
                InputState.Change(virtualMouse, mouseState);

                if (rightStick != Vector2.zero)
                {
                    currentInputDevice = 1;
                }

                // Unfocus UI element after click, it gets stuck when using gamepad
                if (cameraControls.GeneralMap.Click.WasReleasedThisFrame())
                {
                    EventSystem.current.SetSelectedGameObject(null);
                }
            }
        }

        void HandleTouchInput()
        {
            // Movement Touch
            if (ETouch.Touch.activeTouches.Count == 1 && !isOverUI) // Middle or touch
            {
                ETouch.Touch touch = ETouch.Touch.activeTouches[0];

                if (fromDoubleTouchToSingle)
                {
                    fromDoubleTouchToSingle = false;
                    dragStartPosition = PlaneRayCast(touch.screenPosition);
                }

                switch (touch.phase)
                {
                    // Touch started
                    case UnityEngine.InputSystem.TouchPhase.Began:
                        if (!turnOffTouchMove)
                        {
                            dragStartPosition = PlaneRayCast(touch.screenPosition);
                        }

                        break;

                    // Touch moved
                    case UnityEngine.InputSystem.TouchPhase.Moved:
                        if (!turnOffTouchMove)
                        {
                            dragCurrentPosition = PlaneRayCast(touch.screenPosition);
                            newPosition = transform.position + dragStartPosition - dragCurrentPosition;
                        }

                        break;

                    // Touch lifted
                    case UnityEngine.InputSystem.TouchPhase.Ended:

                        break;
                }
            }
            // Zoom and Rotation
            else if (ETouch.Touch.activeTouches.Count == 2)
            {
                ETouch.Touch touch = ETouch.Touch.activeTouches[0];
                ETouch.Touch touch2 = ETouch.Touch.activeTouches[1];

                // Zoom
                if (Vector2.Dot(touch.delta.normalized, touch2.delta.normalized) < -0.85f)
                {
                    // Get touch positions on plane
                    Vector2 prevPositionTouch = touch.screenPosition - touch.delta;
                    Vector2 prevPositionTouch2 = touch2.screenPosition - touch2.delta;

                    Vector3 currentTouch = PlaneRayCast(touch.screenPosition);
                    Vector3 prevTouch = PlaneRayCast(prevPositionTouch);

                    Vector3 currentTouch2 = PlaneRayCast(touch2.screenPosition);
                    Vector3 prevTouch2 = PlaneRayCast(prevPositionTouch2);

                    // Get distances
                    float initialDistance = Vector3.Distance(prevTouch, prevTouch2);
                    float currentDistance = Vector3.Distance(currentTouch, currentTouch2);

                    float deltaDistance = initialDistance - currentDistance;

                    // Do zoom
                    newZoom -= deltaDistance * zoomAmount * touchZoomSensitivity;
                    cursorPosition = Vector2.Lerp(touch.screenPosition, touch2.screenPosition, 0.5f);
                }
                // Rotation
                else if (Vector2.Dot(touch.delta.normalized, touch2.delta.normalized) > 0.85f)
                {
                    // Vertical or Horizontal
                    float angle = Vector2.Angle(touch.delta.normalized, new Vector2(0, 1));

                    // Horizontal - rotation
                    if (angle > 35 && angle < 145)
                    {
                        newRotation += currentUpVector * (touch.delta.x * (touchRotationSensitivity)) * multiplier2D;
                    }
                    // Vertical - tilt
                    else
                    {
                        newRotation += new Vector3(1, 0, 0) * -(touch.delta.y * (touchRotationSensitivity));
                    }
                }

                fromDoubleTouchToSingle = true;
            }
        }

        void HandleMouseInput()
        {
            if (currentInputDevice == 0 && !isOverUI)
            {
                // Movement Mouse
                if (Mouse.current.middleButton.wasPressedThisFrame) // Middle
                {
                    dragStartPosition = PlaneRayCast(Mouse.current.position.ReadValue());
                }

                if (Mouse.current.middleButton.IsPressed()) // Middle
                {
                    dragCurrentPosition = PlaneRayCast(Mouse.current.position.ReadValue());
                    newPosition = transform.position + dragStartPosition - dragCurrentPosition;
                }

                // Zoom
                if (Mouse.current.scroll.y.ReadValue() != 0) // Scroll
                {
                    newZoom += Mathf.Clamp(Mouse.current.scroll.y.ReadValue(), -1, 1) * zoomAmount * mouseZoomSensitivity;
                    cursorPosition = Mouse.current.position.ReadValue();
                }

                // Rotation
                if (Mouse.current.rightButton.wasPressedThisFrame) // Right
                {
                    rotateStartPosition = Mouse.current.position.ReadValue();
                }
                if (Keyboard.current.leftAltKey.IsPressed() && Mouse.current.rightButton.IsPressed()) // Right
                {
                    rotateCurrentPosition = Mouse.current.position.ReadValue();
                    Vector3 difference = rotateStartPosition - rotateCurrentPosition;
                    rotateStartPosition = rotateCurrentPosition;

                    newRotation += currentUpVector * -(difference.x * mouseRotationSensitivity) * multiplier2D; // Rotation
                    newRotation += new Vector3(1, 0, 0) * (difference.y * mouseRotationSensitivity); // Tilt
                }
            }
        }

        void CalculateInput()
        {
            // Cursor handling
            if (currentInputDevice == 0 && !cursorHidden)
            {
                Cursor.visible = true;
                virtualCursorTransform.gameObject.SetActive(false);
            }
            else if (currentInputDevice == 1 && !cursorHidden)
            {
                Cursor.visible = false;
                virtualCursorTransform.gameObject.SetActive(true);
            }
            else
            {
                Cursor.visible = false;
                virtualCursorTransform.gameObject.SetActive(false);
            }

            // Follow set position
            if (followTransform != null)
            {
                newPosition = followTransform.position;
                cursorPosition = new Vector2(Screen.width / 2, Screen.height / 2);
            }

            // Position
            newPosition = PositionClamp(newPosition);
            transform.position = Vector3.Lerp(
                transform.position,
                newPosition,
                Time.deltaTime * lerpTime
            );

            // Zoom, based on cursor position
            if (newZoom != cameraTransform.localPosition)
            {
                Vector3 cursorPoint = PlaneRayCast(cursorPosition);

                newZoom = ZoomClamp(newZoom);
                cameraTransform.localPosition = Vector3.Lerp(
                    cameraTransform.localPosition,
                    newZoom,
                    Time.deltaTime * lerpTime
                );

                Vector3 cursorPointNew = PlaneRayCast(cursorPosition);
                Vector3 direction = (cursorPointNew - cursorPoint).normalized;
                float distance = Vector3.Distance(cursorPoint, cursorPointNew);
                transform.position = PositionClamp(transform.position - direction * distance);
                newPosition -= direction * distance;
            }

            // Rotation
            transform.rotation = Quaternion.Lerp(
                transform.rotation,
                RotationClamp(ref newRotation),
                Time.deltaTime * lerpTime
            );
        }

        public bool IsOverUI(Vector2 screenPosition)
        {
            if (EventSystem.current != null)
            {
                // Check if over UI element
                var click_results = new List<RaycastResult>();
                var click_data = new PointerEventData(EventSystem.current);
                click_data.position = screenPosition;
                EventSystem.current.RaycastAll(click_data, click_results);

                if (click_results.Count == 0)
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }
            else
            {
                return false;
            }
        }

        public Vector3 PlaneRayCast(Vector3 clickPosition, bool centerOfScreen = false)
        {
            // Plane based on If 3D or 2D
            Plane plane;
            if (cameraType == CameraType.TopDown_3D)
            {
                plane = new Plane(Vector3.up, Vector3.zero);
            }
            else
            {
                plane = new Plane(new Vector3(0, 0, 1), Vector3.zero);
            }

            Ray ray;
            if (centerOfScreen)
            {
                ray = cameraTransform.GetComponent<Camera>().ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            }
            else
            {
                ray = cameraTransform.GetComponent<Camera>().ScreenPointToRay(clickPosition);
            }

            float entry;
            if (plane.Raycast(ray, out entry))
            {
                return ray.GetPoint(entry);
            }
            else
            {
                return Vector3.zero;
            }
        }

        private Vector3 ZoomClamp(Vector3 newZoom)
        {
            if (cameraType == CameraType.TopDown_3D)
            {
                newZoom[2] = Mathf.Clamp(newZoom[2], -zoomMax, -zoomMin);//newZoom[1] = Mathf.Clamp(newZoom[1], zoomMin, zoomMax);
                                                                         //newZoom[2] = zoomAmount[2] / zoomAmount[1] * newZoom[1];
                return newZoom;
            }
            else
            {
                newZoom[2] = Mathf.Clamp(newZoom[2], -zoomMax, -zoomMin);
                return newZoom;
            }
        }

        private Vector3 PositionClamp(Vector3 newPosition)
        {
            if (boundaryType == BoundaryType.Square)
            {
                // Get camera bounds
                float distance = Mathf.Abs(Utils.cachedMainCamera.transform.position.y - 0); // Distance to ground

                float camHeight = 2.0f * distance * Mathf.Tan(Utils.cachedMainCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                float camWidth = camHeight * Utils.cachedMainCamera.aspect;

                // Set bounds - Strategy Core specific
                newPosition[0] = Mathf.Clamp(newPosition[0], -squareLimits[3] + camWidth / 2, squareLimits[1] - camWidth / 2);
                
                if (cameraType == CameraType.TopDown_3D)
                {
                    newPosition[2] = Mathf.Clamp(newPosition[2], -squareLimits[2] + camHeight / 2, squareLimits[0] - camHeight / 2);
                }
                else
                {
                    newPosition[1] = Mathf.Clamp(newPosition[1], -squareLimits[2] + camHeight / 2, squareLimits[0] - camHeight / 2);
                }

                // Set bounds - Old
                // newPosition[0] = Mathf.Clamp(newPosition[0], -squareLimits[3], squareLimits[1]);
                // if (cameraType == CameraType.TopDown_3D)
                // {
                //     newPosition[2] = Mathf.Clamp(newPosition[2], -squareLimits[2], squareLimits[0]);
                // }
                // else
                // {
                //     newPosition[1] = Mathf.Clamp(newPosition[1], -squareLimits[2], squareLimits[0]);
                // }
            }
            else
            {
                if (Vector3.Distance(newPosition, new Vector3(0, floorLevel, 0)) > circleLimits)
                {
                    Vector3 direction = (newPosition - new Vector3(0, floorLevel, 0)).normalized;
                    newPosition = direction * circleLimits;
                }
            }
            return newPosition;
        }

        private Quaternion RotationClamp(ref Vector3 newRotation)
        {
            newRotation[0] = Mathf.Clamp(newRotation[0], tiltMinMax[0], tiltMinMax[1]);

            if (cameraType == CameraType.TopDown_3D)
            {
                if (rotationMinMax[0] != 0)
                {
                    newRotation[1] = Mathf.Clamp(newRotation[1], rotationMinMax[0], rotationMinMax[1]);
                }

                return Quaternion.Euler(newRotation);
            }
            else
            {
                if (rotationMinMax[0] != 0)
                {
                    newRotation[2] = Mathf.Clamp(newRotation[2], rotationMinMax[0], rotationMinMax[1]);
                }

                return Quaternion.AngleAxis(newRotation[2], Vector3.forward)
                     * Quaternion.AngleAxis(newRotation[1], Vector3.up)
                     * Quaternion.AngleAxis(newRotation[0], Vector3.right);
            }
        }

        public void HideCursor()
        {
            Cursor.visible = false;
            virtualCursorTransform.gameObject.SetActive(false);
            cursorHidden = true;
        }

        public void ShowCursor()
        {
            if (currentInputDevice == 0)
            {
                Cursor.visible = true;
                virtualCursorTransform.gameObject.SetActive(false);
            }
            else if (currentInputDevice == 1)
            {
                Cursor.visible = false;
                virtualCursorTransform.gameObject.SetActive(true);
            }
            cursorHidden = false;
        }

        public void TouchMove_OFF()
        {
            turnOffTouchMove = true;
        }

        public void TouchMove_ON()
        {
            turnOffTouchMove = false;
        }

        public void SetCursorPosition(Vector2 position)
        {
            virtualCursorTransform.position = position;
            Mouse.current.WarpCursorPosition(position);
        }

        public void SetCursorPosition(bool center)
        {
            virtualCursorTransform.position = new Vector2(Screen.width / 2, Screen.height / 2);
            Mouse.current.WarpCursorPosition(new Vector2(Screen.width / 2, Screen.height / 2));
        }

        // Click, LongClick and DoubleClick ---------------------------------------------------------------------------------------------------------------------------------------------------------
        private Vector2 touchPosition;
        [Tooltip("When on touch finger moves a little do not reset long press")]
        [SerializeField] private float touchDistanceReset = 15f;

        // Long click parameters
        [SerializeField] private float longClickDuration = 0.65f; // Always should be higher than doubleCLickInterval
        private float currentClickDuration = 0;
        private bool clickPressed = false;

        // DoubleClick parameters
        [SerializeField] private float doubleClickTime = 0.4f;
        private float secondClickTimeout = -1;

        // Handle clicks, Input.touchCount < 2 = double finger touch is treated as MouseButton
        private void HandleClicks()
        {
            // Click down
            if (ClickButtonDown())
            {
                // LongClick
                currentClickDuration = 0;
                clickPressed = true;

                // DoubleClick
                if (PlayerControl.instance.mode == PCMode.Default && PlayerControl.isCursorOverUI == false)
                {
                    if (secondClickTimeout < 0)
                    {
                        // This is the first click, calculate the timeout
                        touchPosition = GetCursorPosition();
                        secondClickTimeout = Time.time + doubleClickTime;

                        // Strategy core specific
                        if (PlayerControl.instance != null) PlayerControl.instance.dblClickUnit = Utils.GetUnitAtCursor();
                    }
                    else
                    {
                        // Double click performed
                        if (Time.time < secondClickTimeout)
                        {
                            // Strategy core specific: Distance check turned off
                            if (onDoubleClick != null) // && Vector2.Distance(touchPosition, GetCursorPosition()) < touchDistanceReset)
                            {
                                onDoubleClick(GetCursorPosition());
                            }

                            secondClickTimeout = -1; // Reset the timeout
                        }
                    }
                }
            }

            // Click pressed
            if (clickPressed && ClickButtonPressed())
            {
                currentClickDuration += Time.deltaTime;

                // Long click performed
                if (currentClickDuration >= longClickDuration)
                {
                    if (onLongClick != null)
                    {
                        onLongClick(GetCursorPosition());
                    }

                    clickPressed = false;
                }

                // Long click reset if finger moves
                if (ETouch.Touch.activeTouches.Count == 1 && ETouch.Touch.activeTouches[0].phase == UnityEngine.InputSystem.TouchPhase.Moved)
                {
                    if (Vector2.Distance(touchPosition, GetCursorPosition()) > touchDistanceReset)
                    {
                        clickPressed = false;
                    }
                }
            }

            // Click up
            if (clickPressed && ClickButtonUp())
            {
                clickPressed = false;

                // Single click performed, fire event
                if (secondClickTimeout != -1 && currentClickDuration < longClickDuration)
                {
                    if (onClick != null)
                    {
                        onClick(GetCursorPosition());
                    }
                }
            }

            // Wait for second click, cancel if passes specified time
            if (secondClickTimeout > 0 && Time.time >= secondClickTimeout)
            {
                secondClickTimeout = -1;
            }
        }

        // Returns current cursor position - Mouse, Touch[0] or gamepad
        public Vector2 GetCursorPosition()
        {
            if (ETouch.Touch.activeTouches.Count > 0)
            {
                // Touch
                return ETouch.Touch.activeTouches[0].screenPosition;
            }
            else if (currentInputDevice == 0 && Mouse.current != null)
            {
                // Mouse
                return Mouse.current.position.ReadValue();
            }
            else
            {
                // Gamepad
                return virtualCursorTransform.anchoredPosition;
            }
        }

        // Left mouse or Touch screen down
        public static bool ClickButtonDown()
        {
            if (ETouch.Touch.activeTouches.Count < 2 && ((ETouch.Touch.activeTouches.Count == 1 && ETouch.Touch.activeTouches[0].phase == UnityEngine.InputSystem.TouchPhase.Began) || cameraControls.GeneralMap.Click.WasPressedThisFrame()))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        // Left mouse or Touch screen pressed
        public static bool ClickButtonPressed()
        {
            if (ETouch.Touch.activeTouches.Count < 2 && (ETouch.Touch.activeTouches.Count == 1 || cameraControls.GeneralMap.Click.IsPressed()))
            {

                return true;
            }
            else
            {
                return false;
            }
        }

        // Left mouse or Touch screen up
        public static bool ClickButtonUp()
        {
            if (ETouch.Touch.activeTouches.Count < 2 && ((ETouch.Touch.activeTouches.Count == 1 && ETouch.Touch.activeTouches[0].phase == UnityEngine.InputSystem.TouchPhase.Ended) || cameraControls.GeneralMap.Click.WasReleasedThisFrame()))
            {
                return true;
            }
            else
            {
                return false;
            }
        }


        // Snap to Grid
        public Vector3 SnapToGrid(Vector2 gridSize, Vector3 position)
        {
            if (gridSize.x != 0 && gridSize.y != 0)
            {
                if (cameraType == CameraType._2D)
                {
                    return new Vector3(
                        RoundToNearestGrid(gridSize.x, position[0]),
                        RoundToNearestGrid(gridSize.y, position[1]),
                        position[2]);
                }
                else
                {
                    return new Vector3(
                        RoundToNearestGrid(gridSize.x, position[0]),
                        position[1],
                        RoundToNearestGrid(gridSize.y, position[2]));
                }
            }
            else
            {
                return position;
            }

        }

        private static float RoundToNearestGrid(float gridSize, float pos)
        {
            float xDiff = pos % gridSize;
            pos -= xDiff;
            if (xDiff > (gridSize / 2))
            {
                pos += gridSize;
            }
            return pos;
        }
    }
}
