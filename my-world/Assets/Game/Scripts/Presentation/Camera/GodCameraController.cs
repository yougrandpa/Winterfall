using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MyAiWorld.Presentation.CameraControl
{
    [DefaultExecutionOrder(100)]
    public sealed class GodCameraController : MonoBehaviour
    {
        private int _pixelsPerUnit = 32;
        private const int MinPixelScale = 1;
        private const int MaxPixelScale = 6;

        private Camera _camera;
        private int _mapWidth;
        private int _mapHeight;
        private int _pixelsPerArtPixel = 2;
        private int _lastScreenHeight;
        private bool _dragging;
        private Vector3 _lastMousePosition;
        private Vector3 _position;
        private Transform _followTarget;
        public bool IsFollowing => _followTarget != null;
        public int PixelScale => _pixelsPerArtPixel;

        public void Initialize(Camera camera, int mapWidth, int mapHeight, int initialPixelScale, int pixelsPerUnit = 32)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            _pixelsPerUnit = Mathf.Max(1, pixelsPerUnit);
            _mapWidth = mapWidth;
            _mapHeight = mapHeight;
            _pixelsPerArtPixel = Mathf.Clamp(initialPixelScale, MinPixelScale, MaxPixelScale);
            _lastScreenHeight = Screen.height;
            _camera.orthographic = true;
            _followTarget = null;
            _dragging = false;
            _position = new Vector3(mapWidth * 0.5f, mapHeight * 0.5f, -10f);
            ApplyPixelScale();
            ClampAndSnap();
        }

        private void Update()
        {
            if (_camera == null)
            {
                return;
            }

            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (Screen.height != _lastScreenHeight)
            {
                _lastScreenHeight = Screen.height;
                ApplyPixelScale();
            }

            HandleDrag(overUi);
            HandleKeyboardPan();
            HandleZoom(overUi);
            ClampAndSnap();
        }

        private void HandleDrag(bool overUi)
        {
            if (!overUi && (Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2)))
            {
                _dragging = true;
                _lastMousePosition = Input.mousePosition;
            }

            if (!Input.GetMouseButton(1) && !Input.GetMouseButton(2))
            {
                _dragging = false;
            }

            if (!_dragging) { return; }
            if (overUi)
            {
                // Do not accumulate the distance travelled across a HUD panel.
                _lastMousePosition = Input.mousePosition;
                return;
            }

            Vector3 current = Input.mousePosition;
            Vector3 delta = current - _lastMousePosition;
            _lastMousePosition = current;
            float unitsPerPixel = (2f * _camera.orthographicSize) / Mathf.Max(1f, Screen.height);
            if (delta.sqrMagnitude > 0f)
            {
                StopFollowing();
                _position -= new Vector3(delta.x, delta.y, 0f) * unitsPerPixel;
            }
        }

        private void HandleKeyboardPan()
        {
            float x = 0f;
            float y = 0f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
            {
                x -= 1f;
            }

            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
            {
                x += 1f;
            }

            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
            {
                y -= 1f;
            }

            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
            {
                y += 1f;
            }

            if (x == 0f && y == 0f)
            {
                return;
            }

            float speed = _camera.orthographicSize * 1.25f;
            Pan(new Vector2(x, y).normalized * speed * Time.unscaledDeltaTime);
        }

        private void HandleZoom(bool overUi)
        {
            if (overUi)
            {
                return;
            }

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Approximately(scroll, 0f))
            {
                return;
            }

            ZoomAt(Input.mousePosition, scroll > 0f ? 1 : -1);
        }

        public void ZoomAt(Vector2 screenPosition, int steps)
        {
            int next = Mathf.Clamp(_pixelsPerArtPixel + steps, MinPixelScale, MaxPixelScale);
            if (_camera == null || next == _pixelsPerArtPixel) { return; }
            Vector3 before = _camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, 10f));
            _pixelsPerArtPixel = next;
            ApplyPixelScale();
            Vector3 after = _camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, 10f));
            // Following deliberately keeps the villager centered during zoom.
            if (!IsFollowing) { _position += before - after; }
            ClampAndSnap();
        }

        public void Pan(Vector2 delta)
        {
            StopFollowing();
            _position += new Vector3(delta.x, delta.y, 0f);
            ClampAndSnap();
        }

        public void Follow(Transform target)
        {
            _followTarget = target;
            if (target != null) { _position = target.position; ClampAndSnap(); }
        }

        public void StopFollowing() { _followTarget = null; }

        private void LateUpdate()
        {
            if (_followTarget == null) { return; }
            _position = _followTarget.position;
            ClampAndSnap();
        }

        public void Focus(Vector3 position)
        {
            if (_camera == null)
            {
                return;
            }

            StopFollowing();
            _position = position;
            ClampAndSnap();
        }

        public void SetPixelScale(int scale)
        {
            int next = Mathf.Clamp(scale, MinPixelScale, MaxPixelScale);
            if (next == _pixelsPerArtPixel)
            {
                return;
            }

            _pixelsPerArtPixel = next;
            ApplyPixelScale();
            ClampAndSnap();
        }

        private void ApplyPixelScale()
        {
            if (_camera == null)
            {
                return;
            }

            int screenHeight = Mathf.Max(1, Screen.height);
            _camera.orthographicSize = screenHeight / (2f * _pixelsPerUnit * _pixelsPerArtPixel);
        }

        private void ClampAndSnap()
        {
            if (_camera == null || _mapWidth <= 0 || _mapHeight <= 0)
            {
                return;
            }

            float halfHeight = _camera.orthographicSize;
            float aspect = _camera.aspect > 0f ? _camera.aspect : 16f / 9f;
            float halfWidth = halfHeight * aspect;

            float minX = halfWidth;
            float maxX = _mapWidth - halfWidth;
            float minY = halfHeight;
            float maxY = _mapHeight - halfHeight;

            Vector3 position = _position;
            position.x = minX > maxX ? _mapWidth * 0.5f : Mathf.Clamp(position.x, minX, maxX);
            position.y = minY > maxY ? _mapHeight * 0.5f : Mathf.Clamp(position.y, minY, maxY);
            position.z = -10f;

            _position = position;
            float artPixel = 1f / _pixelsPerUnit;
            position.x = Mathf.Round(position.x / artPixel) * artPixel;
            position.y = Mathf.Round(position.y / artPixel) * artPixel;

            _camera.transform.position = position;
        }
    }
}
