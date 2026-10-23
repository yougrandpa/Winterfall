using System;
using MyAiWorld.App;
using MyAiWorld.Presentation.CameraControl;
using MyAiWorld.Presentation.Rendering;
using MyAiWorld.Presentation.Selection;
using MyAiWorld.Presentation.UI;
using MyAiWorld.Sim.World;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MyAiWorld.Presentation
{
    [DefaultExecutionOrder(-1000)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField]
        private WorldConfigAsset config;

        private bool _ownsCamera;
        private int _worldRevision;

        public WorldConfigAsset ConfigAsset
        {
            get { return config; }
            set { config = value; }
        }

        public GameSession Session { get; private set; }
        public SimulationRunner Runner { get; private set; }
        public WorldPresenter Presenter { get; private set; }
        public WorldSelectionController Selection { get; private set; }
        public GodHud Hud { get; private set; }
        public Camera PlayerCamera { get; private set; }
        public int NpcViewCount => Presenter != null ? Presenter.NpcViewCount : 0;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            QualitySettings.antiAliasing = 0;
            WorldConfigData configData = config != null ? config.ToData() : WorldConfigData.CreateDefault();

            try
            {
                Session = new GameSession(configData);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                enabled = false;
                return;
            }

            Runner = gameObject.AddComponent<SimulationRunner>();
            Runner.Initialize(Session, configData);

            PlayerCamera = SetupCamera(configData);
            EnsureEventSystem();

            var presenterObject = new GameObject("WorldPresenter");
            presenterObject.transform.SetParent(transform, false);
            Presenter = presenterObject.AddComponent<WorldPresenter>();
            Presenter.Initialize(Session, Runner);

            var selectionObject = new GameObject("Selection");
            selectionObject.transform.SetParent(transform, false);
            Selection = selectionObject.AddComponent<WorldSelectionController>();
            Selection.Initialize(PlayerCamera, Presenter);

            var hudObject = new GameObject("GodHud");
            hudObject.transform.SetParent(transform, false);
            Hud = hudObject.AddComponent<GodHud>();
            Hud.Initialize(Session, Runner, Presenter, Selection);

            Debug.Log("My AI World started. Seed=" + Session.Config.Seed +
                      ", NPCs=" + Session.ReadModel.NpcCount +
                      ", Log=" + Session.LogPath);
        }

        private void Update()
        {
            if (Session != null && _worldRevision != Session.WorldRevision)
            {
                _worldRevision = Session.WorldRevision;
                SetupCamera(Session.Config);
                Selection.Select(null);
            }
        }

        private Camera SetupCamera(WorldConfigData configData)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.transform.SetParent(transform, false);
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
                _ownsCamera = true;
            }

            camera.orthographic = true;
            camera.allowMSAA = false;
            camera.allowHDR = false;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.05f, 0.055f, 1f);

            var controller = camera.GetComponent<GodCameraController>();
            if (controller == null)
            {
                controller = camera.gameObject.AddComponent<GodCameraController>();
            }

            controller.Initialize(camera, configData.Width, configData.Height, 1, PastoralArtConfig.Load().pixelsPerUnit);
            return camera;
        }

        public void CaptureReviewScreenshot(string path)
        {
            if (!isActiveAndEnabled)
            {
                ScreenCapture.CaptureScreenshot(path);
                return;
            }

            StartCoroutine(CaptureReviewRoutine(path));
        }

        private System.Collections.IEnumerator CaptureReviewRoutine(string path)
        {
            // Let the Game View regain focus and render one full frame before the
            // back buffer is read; otherwise the editor can return a stale frame.
            yield return null;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("Review screenshot captured: " + path +
                      " tick=" + Session.ReadModel.Tick + " date=" + Session.ReadModel.Date.ClockText);
        }

        private void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null)
            {
                return;
            }

            var eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.transform.SetParent(transform, false);
            eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<StandaloneInputModule>();
        }

        private void OnDestroy()
        {
            if (Session != null)
            {
                Session.Dispose();
                Session = null;
            }

            if (_ownsCamera && PlayerCamera != null)
            {
                Destroy(PlayerCamera.gameObject);
            }
        }
    }
}
