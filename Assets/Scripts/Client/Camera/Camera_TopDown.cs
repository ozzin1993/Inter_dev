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
    public partial class Camera_TopDown : MonoBehaviour // [Interflow fix 2026-08-01 partial-split] класс разрезан на partial-файлы (задача №11)
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
    }
}
