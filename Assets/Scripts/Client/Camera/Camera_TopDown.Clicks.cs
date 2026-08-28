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
    // Camera_TopDown.Clicks.cs — клики (одиночный/двойной/долгий) + сетка. Вырезано 1:1 из Camera_TopDown.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class Camera_TopDown
    {
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
                if (PlayerControl.Instance.mode == PCMode.Default && PlayerControl.isCursorOverUI == false)
                {
                    if (secondClickTimeout < 0)
                    {
                        // This is the first click, calculate the timeout
                        touchPosition = GetCursorPosition();
                        secondClickTimeout = Time.time + doubleClickTime;

                        // Strategy core specific
                        if (PlayerControl.Instance != null) PlayerControl.Instance.dblClickUnit = Utils.GetUnitAtCursor();
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
