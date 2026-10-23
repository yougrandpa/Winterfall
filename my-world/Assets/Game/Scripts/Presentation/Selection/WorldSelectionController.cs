using System;
using MyAiWorld.Presentation.Rendering;
using MyAiWorld.Sim.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MyAiWorld.Presentation.Selection
{
    public sealed class WorldSelectionController : MonoBehaviour
    {
        private Camera _camera;
        private WorldPresenter _presenter;

        public GridPos? SelectedCell { get; private set; }
        public event Action<GridPos> CellSelected;
        public EntityId? Selected { get; private set; }
        public event Action<EntityId?> SelectionChanged;

        public void Initialize(Camera camera, WorldPresenter presenter)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        }

        private void Update()
        {
            if (_camera == null || _presenter == null)
            {
                return;
            }

            if (!Input.GetMouseButtonDown(0))
            {
                return;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            Vector3 screen = Input.mousePosition;
            Vector3 world = _camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -_camera.transform.position.z));
            NpcView view = _presenter.PickNpc(new Vector2(world.x, world.y));
            Select(view != null ? view.Id : (EntityId?)null);
            if (view == null) { SelectCell(new GridPos(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y))); }
        }

        public void SelectCell(GridPos cell)
        {
            Select(null); SelectedCell = cell; CellSelected?.Invoke(cell);
        }

        public void Select(EntityId? id)
        {
            SelectedCell = null;
            if (Selected.HasValue == id.HasValue &&
                (!Selected.HasValue || Selected.Value == id.Value))
            {
                return;
            }

            Selected = id;
            _presenter.SetSelected(id);
            Action<EntityId?> handler = SelectionChanged;
            if (handler != null)
            {
                handler(id);
            }
        }
    }
}
