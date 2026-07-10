using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using ETouch = UnityEngine.InputSystem.EnhancedTouch;

namespace Camera_TopDownNS
{
    public class InputTesterUSC : MonoBehaviour
    {
        [SerializeField] private GameObject mouse;
        [SerializeField] private GameObject mouseMiddle;
        [SerializeField] private GameObject mouseLeft;
        [SerializeField] private GameObject mouseRight;

        [SerializeField] private GameObject rightStick;
        [SerializeField] private GameObject leftStick;
        [SerializeField] private GameObject dpadLeft;
        [SerializeField] private GameObject dpadRight;
        [SerializeField] private GameObject dpadUp;
        [SerializeField] private GameObject dpadDown;
        [SerializeField] private GameObject shoulderRight;
        [SerializeField] private GameObject shoulderLeft;
        [SerializeField] private GameObject triggerRight;
        [SerializeField] private GameObject triggerLeft;

        [SerializeField] private GameObject phone;
        [SerializeField] private GameObject phoneSingle;
        [SerializeField] private GameObject phoneDouble;

        private int device = 0;
        private InputTesterUVC inputTesterUVC;

        private void Awake()
        {
            inputTesterUVC = new InputTesterUVC();
        }
        private void OnEnable()
        {
            inputTesterUVC.Enable();
        }
        private void OnDisable()
        {
            inputTesterUVC.Disable();
        }

        // Start is called before the first frame update
        void Start()
        {

        }

        // Update is called once per frame
        void Update()
        {
            // Left
            if (inputTesterUVC.Mouse.Left.WasPressedThisFrame())
            {
                Color color = Color.white;
                color.a = 255;
                mouseLeft.GetComponent<RawImage>().color = color;
                device = 0;
            }

            if (inputTesterUVC.Mouse.Left.WasReleasedThisFrame())
            {
                Color color = Color.white;
                color.a = 0;
                mouseLeft.GetComponent<RawImage>().color = color;
                device = 0;
            }

            // Middle
            if (inputTesterUVC.Mouse.Middle.WasPressedThisFrame())
            {
                Color color = Color.white;
                color.a = 255;
                mouseMiddle.GetComponent<RawImage>().color = color;
                device = 0;
            }

            if (inputTesterUVC.Mouse.Middle.WasReleasedThisFrame())
            {
                Color color = Color.white;
                color.a = 0;
                mouseMiddle.GetComponent<RawImage>().color = color;
                device = 0;
            }

            if (Mouse.current.middleButton.IsPressed())
            {
                Color color = Color.white;
                color.a = 255;
                mouseMiddle.GetComponent<RawImage>().color = color;
                device = 0;
            }
            else
            {
                Color color = Color.white;
                color.a = 255;
                mouseMiddle.GetComponent<RawImage>().color = color;
            }

            // Right
            if (inputTesterUVC.Mouse.Right.WasPressedThisFrame())
            {
                Color color = Color.white;
                color.a = 255;
                mouseRight.GetComponent<RawImage>().color = color;
                device = 0;
            }

            if (inputTesterUVC.Mouse.Right.WasReleasedThisFrame())
            {
                Color color = Color.white;
                color.a = 0;
                mouseRight.GetComponent<RawImage>().color = color;
                device = 0;
            }

            // Scroll
            float scroll = inputTesterUVC.Mouse.Scroll.ReadValue<float>();
            if (scroll != 0)
            {
                Color color = Color.white;
                color.a = 255;
                mouseMiddle.GetComponent<RawImage>().color = color;
                device = 0;
            }
            else
            {
                Color color = Color.white;
                color.a = 0;
                mouseMiddle.GetComponent<RawImage>().color = color;
            }


            // GAMEPAD
            Vector2 rightStickVal = inputTesterUVC.Gamepad.RightStick.ReadValue<Vector2>();
            if (rightStickVal != Vector2.zero)
            {
                Color color = Color.white;
                color.a = 255;
                rightStick.GetComponent<RawImage>().color = color;
                device = 1;
            }
            else
            {
                Color color = Color.white;
                color.a = 0;
                rightStick.GetComponent<RawImage>().color = color;
            }

            Vector2 leftStickVal = inputTesterUVC.Gamepad.LeftStick.ReadValue<Vector2>();
            if (leftStickVal != Vector2.zero)
            {
                Color color = Color.white;
                color.a = 255;
                leftStick.GetComponent<RawImage>().color = color;
                device = 1;
            }
            else
            {
                Color color = Color.white;
                color.a = 0;
                leftStick.GetComponent<RawImage>().color = color;
            }

            Vector2 dpadVal = inputTesterUVC.Gamepad.Dpad.ReadValue<Vector2>();
            if (dpadVal == Vector2.zero)
            {
                Color color = Color.white;
                color.a = 0;
                dpadDown.GetComponent<RawImage>().color = color;
                dpadUp.GetComponent<RawImage>().color = color;
                dpadLeft.GetComponent<RawImage>().color = color;
                dpadRight.GetComponent<RawImage>().color = color;
            }
            else if (dpadVal == new Vector2(1, 0))
            {
                Color color = Color.white;
                color.a = 255;
                dpadRight.GetComponent<RawImage>().color = color;
                device = 1;
            }
            else if (dpadVal == new Vector2(-1, 0))
            {
                Color color = Color.white;
                color.a = 255;
                dpadLeft.GetComponent<RawImage>().color = color;
                device = 1;
            }
            else if (dpadVal == new Vector2(0, 1))
            {
                Color color = Color.white;
                color.a = 255;
                dpadUp.GetComponent<RawImage>().color = color;
                device = 1;
            }
            else if (dpadVal == new Vector2(0, -1))
            {
                Color color = Color.white;
                color.a = 255;
                dpadDown.GetComponent<RawImage>().color = color;
                device = 1;
            }

            // Shoulders
            if (inputTesterUVC.Gamepad.RightShoulder.WasPressedThisFrame())
            {
                Color color = Color.white;
                color.a = 255;
                shoulderRight.GetComponent<RawImage>().color = color;
                device = 1;
            }

            if (inputTesterUVC.Gamepad.RightShoulder.WasReleasedThisFrame())
            {
                Color color = Color.white;
                color.a = 0;
                shoulderRight.GetComponent<RawImage>().color = color;
                device = 1;
            }

            if (inputTesterUVC.Gamepad.LeftShoulder.WasPressedThisFrame())
            {
                Color color = Color.white;
                color.a = 255;
                shoulderLeft.GetComponent<RawImage>().color = color;
                device = 1;
            }

            if (inputTesterUVC.Gamepad.LeftShoulder.WasReleasedThisFrame())
            {
                Color color = Color.white;
                color.a = 0;
                shoulderLeft.GetComponent<RawImage>().color = color;
                device = 1;
            }

            // Trigger
            if (inputTesterUVC.Gamepad.LeftTrigger.WasPressedThisFrame())
            {
                Color color = Color.white;
                color.a = 255;
                triggerLeft.GetComponent<RawImage>().color = color;
                device = 1;
            }

            if (inputTesterUVC.Gamepad.LeftTrigger.WasReleasedThisFrame())
            {
                Color color = Color.white;
                color.a = 0;
                triggerLeft.GetComponent<RawImage>().color = color;
                device = 1;
            }

            if (inputTesterUVC.Gamepad.RightTrigger.WasPressedThisFrame())
            {
                Color color = Color.white;
                color.a = 255;
                triggerRight.GetComponent<RawImage>().color = color;
                device = 1;
            }

            if (inputTesterUVC.Gamepad.RightTrigger.WasReleasedThisFrame())
            {
                Color color = Color.white;
                color.a = 0;
                triggerRight.GetComponent<RawImage>().color = color;
                device = 1;
            }

            // Touch
            if (ETouch.Touch.activeTouches.Count == 1)
            {
                Color color = Color.white;
                color.a = 255;
                phoneSingle.GetComponent<RawImage>().color = color;

                color.a = 0;
                phoneDouble.GetComponent<RawImage>().color = color;

                device = 2;
            }
            else if (ETouch.Touch.activeTouches.Count > 1)
            {
                Color color = Color.white;
                color.a = 0;
                phoneSingle.GetComponent<RawImage>().color = color;

                color.a = 255;
                phoneDouble.GetComponent<RawImage>().color = color;

                device = 2;
            }
            else
            {
                Color color = Color.white;
                color.a = 0;
                phoneSingle.GetComponent<RawImage>().color = color;
                phoneDouble.GetComponent<RawImage>().color = color;
            }

            ShowDevice(device);
        }

        private void ShowDevice(int deviceID)
        {
            if (deviceID == 0)
            {
                mouseLeft.SetActive(true);
                dpadDown.SetActive(false);
                phone.SetActive(false);
            }
            else if (deviceID == 1)
            {
                dpadDown.SetActive(true);
                mouseLeft.SetActive(false);
                phone.SetActive(false);
            }
            else
            {
                mouseLeft.SetActive(false);
                dpadDown.SetActive(false);
                phone.SetActive(true);
            }
        }
    }
}
